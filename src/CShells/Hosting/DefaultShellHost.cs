using System.Reflection;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Features.Validation;
using CShells.Management;
using CShells.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Hosting;

/// <summary>
/// Default implementation of <see cref="IShellHost"/> that builds and caches per-shell
/// <see cref="IServiceProvider"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Shell features (<see cref="IShellFeature"/> implementations) are instantiated using the
/// application's root <see cref="IServiceProvider"/> via <see cref="ActivatorUtilities.CreateInstance"/>,
/// with <see cref="ShellSettings"/> passed as an explicit parameter. This means:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Feature constructors may only depend on root-level services (logging, configuration, etc.) and ShellSettings.</description>
///   </item>
///   <item>
///     <description>No temporary shell ServiceProviders are created during feature configuration.</description>
///   </item>
///   <item>
///     <description>Shell services are registered purely via IServiceCollection in ConfigureServices.</description>
///   </item>
/// </list>
/// </remarks>
public class DefaultShellHost : IShellHost, IShellHostInitializer, IAsyncDisposable
{
    private readonly IServiceProvider _rootProvider;
    private readonly IServiceCollection _rootServices;
    private readonly IShellFeatureFactory _featureFactory;
    private readonly IShellServiceExclusionRegistry _exclusionRegistry;
    private readonly ShellRuntimeStateStore _runtimeStateStore;
    private readonly RuntimeFeatureCatalog _runtimeFeatureCatalog;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly FeatureDependencyResolver _dependencyResolver = new();
    private readonly ILogger<DefaultShellHost> _logger;
    private readonly object _buildLock = new();
    private bool _disposed;

    // Cached copy of root service descriptors for efficient bulk-copy to shell service collections.
    // This avoids re-enumerating the root IServiceCollection for each shell.
    private List<ServiceDescriptor>? _cachedRootDescriptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultShellHost"/> class with custom assemblies and root service collection.
    /// </summary>
    /// <param name="shellSettingsCache">The cache providing access to shell settings.</param>
    /// <param name="assemblies">The assemblies to scan for features.</param>
    /// <param name="rootProvider">
    /// The application's root <see cref="IServiceProvider"/> used to instantiate <see cref="IShellFeature"/> implementations.
    /// Feature constructors can resolve root-level services (logging, configuration, etc.).
    /// </param>
    /// <param name="rootServicesAccessor">
    /// An accessor to the root <see cref="IServiceCollection"/>. Root service registrations
    /// are copied into each shell's service collection, enabling inheritance of root services.
    /// </param>
    /// <param name="featureFactory">The factory used to create shell feature instances.</param>
    /// <param name="exclusionRegistry"> The registry of service types to exclude from root service inheritance.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="shellSettingsCache"/>, <paramref name="assemblies"/>, <paramref name="rootProvider"/>, <paramref name="rootServicesAccessor"/>, or <paramref name="featureFactory"/> is null.</exception>
    public DefaultShellHost(
        IShellSettingsCache shellSettingsCache,
        IEnumerable<Assembly> assemblies,
        IServiceProvider rootProvider,
        IRootServiceCollectionAccessor rootServicesAccessor,
        IShellFeatureFactory featureFactory,
        IShellServiceExclusionRegistry exclusionRegistry,
        ILogger<DefaultShellHost>? logger = null)
        : this(shellSettingsCache, assemblies, rootProvider, rootServicesAccessor, featureFactory, exclusionRegistry, activateExistingShells: true, runtimeFeatureCatalog: null, runtimeStateStore: null, notificationPublisher: null, logger)
    {
    }

    internal DefaultShellHost(
        IShellSettingsCache shellSettingsCache,
        IEnumerable<Assembly> assemblies,
        IServiceProvider rootProvider,
        IRootServiceCollectionAccessor rootServicesAccessor,
        IShellFeatureFactory featureFactory,
        IShellServiceExclusionRegistry exclusionRegistry,
        bool activateExistingShells,
        RuntimeFeatureCatalog? runtimeFeatureCatalog = null,
        ShellRuntimeStateStore? runtimeStateStore = null,
        INotificationPublisher? notificationPublisher = null,
        ILogger<DefaultShellHost>? logger = null)
    {
        var cache = Guard.Against.Null(shellSettingsCache);
        _rootProvider = Guard.Against.Null(rootProvider);
        _rootServices = Guard.Against.Null(rootServicesAccessor).Services;
        _featureFactory = Guard.Against.Null(featureFactory);
        _exclusionRegistry = Guard.Against.Null(exclusionRegistry);
        _runtimeStateStore = runtimeStateStore ?? new ShellRuntimeStateStore();
        _notificationPublisher = notificationPublisher ?? new DefaultNotificationPublisher(rootProvider);
        _logger = logger ?? NullLogger<DefaultShellHost>.Instance;

        var fixedAssemblies = Guard.Against.Null(assemblies).ToList().AsReadOnly();
        _runtimeFeatureCatalog = runtimeFeatureCatalog ?? new RuntimeFeatureCatalog(
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>(fixedAssemblies),
            rootProvider.GetService<ILogger<RuntimeFeatureCatalog>>());

        SeedDesiredState(cache.GetAll());
        if (activateExistingShells)
        {
            _runtimeFeatureCatalog.EnsureInitializedAsync().GetAwaiter().GetResult();
            InitializeAppliedRuntimes(cache.GetAll());
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultShellHost"/> class with a deferred assembly resolver.
    /// </summary>
    /// <param name="shellSettingsCache">The cache providing access to shell settings.</param>
    /// <param name="assemblyResolver">The asynchronous resolver that provides assemblies to scan for features.</param>
    /// <param name="rootProvider">The application's root service provider.</param>
    /// <param name="rootServicesAccessor">Accessor for the root service collection.</param>
    /// <param name="featureFactory">The factory used to create shell feature instances.</param>
    /// <param name="exclusionRegistry">The registry of service types to exclude from root service inheritance.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public DefaultShellHost(
        IShellSettingsCache shellSettingsCache,
        Func<CancellationToken, Task<IReadOnlyCollection<Assembly>>> assemblyResolver,
        IServiceProvider rootProvider,
        IRootServiceCollectionAccessor rootServicesAccessor,
        IShellFeatureFactory featureFactory,
        IShellServiceExclusionRegistry exclusionRegistry,
        ILogger<DefaultShellHost>? logger = null)
        : this(shellSettingsCache, assemblyResolver, rootProvider, rootServicesAccessor, featureFactory, exclusionRegistry, seedDesiredStateFromCache: true, runtimeFeatureCatalog: null, runtimeStateStore: null, notificationPublisher: null, logger)
    {
    }

    internal DefaultShellHost(
        IShellSettingsCache shellSettingsCache,
        Func<CancellationToken, Task<IReadOnlyCollection<Assembly>>> assemblyResolver,
        IServiceProvider rootProvider,
        IRootServiceCollectionAccessor rootServicesAccessor,
        IShellFeatureFactory featureFactory,
        IShellServiceExclusionRegistry exclusionRegistry,
        bool seedDesiredStateFromCache,
        RuntimeFeatureCatalog? runtimeFeatureCatalog = null,
        ShellRuntimeStateStore? runtimeStateStore = null,
        INotificationPublisher? notificationPublisher = null,
        ILogger<DefaultShellHost>? logger = null)
    {
        var cache = Guard.Against.Null(shellSettingsCache);
        _rootProvider = Guard.Against.Null(rootProvider);
        _rootServices = Guard.Against.Null(rootServicesAccessor).Services;
        _featureFactory = Guard.Against.Null(featureFactory);
        _exclusionRegistry = Guard.Against.Null(exclusionRegistry);
        _runtimeStateStore = runtimeStateStore ?? new ShellRuntimeStateStore();
        _runtimeFeatureCatalog = runtimeFeatureCatalog ?? new RuntimeFeatureCatalog(Guard.Against.Null(assemblyResolver), rootProvider.GetService<ILogger<RuntimeFeatureCatalog>>());
        _notificationPublisher = notificationPublisher ?? new DefaultNotificationPublisher(rootProvider);
        _logger = logger ?? NullLogger<DefaultShellHost>.Instance;

        if (seedDesiredStateFromCache)
            SeedDesiredState(cache.GetAll());
    }

    internal ShellRuntimeStateStore RuntimeStateStore => _runtimeStateStore;

    internal RuntimeFeatureCatalog RuntimeFeatureCatalog => _runtimeFeatureCatalog;

    /// <summary>
    /// Ensures that feature discovery has completed for this shell host.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    /// <inheritdoc />
    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return _runtimeFeatureCatalog.EnsureInitializedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ShellContext DefaultShell
    {
        get
        {
            ThrowIfDisposed();

            var defaultId = new ShellId(ShellConstants.DefaultShellName);
            if (_runtimeStateStore.HasDesiredShell(defaultId))
                return GetShell(defaultId);

            var firstAppliedShell = AllShells.FirstOrDefault();
            return firstAppliedShell ?? throw new InvalidOperationException("No applied shells are currently available.");
        }
    }

    /// <inheritdoc />
    public ShellContext GetShell(ShellId id)
    {
        ThrowIfDisposed();

        var record = _runtimeStateStore.Get(id);
        if (record is null || !record.HasAppliedRuntime)
        {
            throw new KeyNotFoundException($"Shell with Id '{id}' does not have a committed applied runtime.");
        }

        if (record.AppliedContext is not null)
            return record.AppliedContext;

        lock (_buildLock)
        {
            record = _runtimeStateStore.Get(id);
            if (record is null || !record.HasAppliedRuntime)
            {
                throw new KeyNotFoundException($"Shell with Id '{id}' does not have a committed applied runtime.");
            }

            if (record.AppliedContext is not null)
                return record.AppliedContext;

            var orderedFeatures = ResolveFeatureDependencies(record.AppliedSettings!, record.AppliedCatalog!.FeatureMap);
            var context = CreateShellContext(record.AppliedSettings!, orderedFeatures, record.AppliedCatalog.FeatureDescriptors);
            _runtimeStateStore.SetAppliedContext(id, context);
            return context;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ShellContext> AllShells
    {
        get
        {
            ThrowIfDisposed();

            return _runtimeStateStore
                .GetAll()
                .Where(record => record.HasAppliedRuntime)
                .Select(record => GetShell(record.ShellId))
                .ToList()
                .AsReadOnly();
        }
    }

    internal ShellCandidateBuildResult BuildCandidate(ShellRuntimeRecord record, RuntimeFeatureCatalogSnapshot catalogSnapshot)
    {
        Guard.Against.Null(record);
        Guard.Against.Null(catalogSnapshot);

        try
        {
            var missingFeatures = record.DesiredSettings.EnabledFeatures
                .Where(featureName => !catalogSnapshot.FeatureMap.ContainsKey(featureName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingFeatures.Length > 0)
            {
                _logger.LogWarning(
                    "Shell '{ShellId}' is missing feature(s) '{MissingFeatures}' from the runtime feature catalog. Activating with available features only.",
                    record.ShellId,
                    string.Join(", ", missingFeatures));
            }

            // Build with available features only, filtering out missing ones
            var availableSettings = missingFeatures.Length > 0
                ? CreateSettingsWithAvailableFeatures(record.DesiredSettings, missingFeatures)
                : record.DesiredSettings;

            var orderedFeatures = availableSettings.EnabledFeatures.Count > 0
                ? ResolveFeatureDependencies(availableSettings, catalogSnapshot.FeatureMap)
                : [];

            var context = CreateShellContext(record.DesiredSettings, orderedFeatures, catalogSnapshot.FeatureDescriptors, missingFeatures);
            return new ShellCandidateBuildResult(record.ShellId, record.DesiredGeneration, record.DesiredSettings, catalogSnapshot, context, null, missingFeatures);
        }
        catch (FeatureNotFoundException ex)
        {
            _logger.LogError(ex, "Failed to build candidate runtime for shell '{ShellId}'", record.ShellId);
            return new ShellCandidateBuildResult(record.ShellId, record.DesiredGeneration, record.DesiredSettings, catalogSnapshot, null, ex.Message, []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build candidate runtime for shell '{ShellId}'", record.ShellId);
            return new ShellCandidateBuildResult(record.ShellId, record.DesiredGeneration, record.DesiredSettings, catalogSnapshot, null, ex.Message, []);
        }
    }

    private static ShellSettings CreateSettingsWithAvailableFeatures(ShellSettings desiredSettings, IReadOnlyCollection<string> missingFeatures)
    {
        var missingSet = new HashSet<string>(missingFeatures, StringComparer.OrdinalIgnoreCase);
        var availableFeatures = desiredSettings.EnabledFeatures
            .Where(f => !missingSet.Contains(f))
            .ToList();

        var filtered = new ShellSettings
        {
            Id = desiredSettings.Id,
            EnabledFeatures = availableFeatures,
            ConfigurationData = desiredSettings.ConfigurationData.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var configurator in desiredSettings.FeatureConfigurators)
        {
            filtered.FeatureConfigurators[configurator.Key] = configurator.Value;
        }

        return filtered;
    }

    internal async Task CommitCandidateAsync(
        ShellCandidateBuildResult candidate,
        bool publishLifecycleNotifications = true,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(candidate);

        if (!candidate.IsReadyToCommit)
        {
            throw new InvalidOperationException($"Shell '{candidate.ShellId}' does not have a ready-to-commit runtime candidate.");
        }

        var previousRecord = _runtimeStateStore.Get(candidate.ShellId);
        var previousContext = previousRecord?.AppliedContext;

        if (publishLifecycleNotifications && previousContext is not null)
        {
            await _notificationPublisher.PublishAsync(new ShellDeactivating(previousContext), strategy: null, cancellationToken).ConfigureAwait(false);
        }

        var commit = _runtimeStateStore.CommitAppliedRuntime(
            candidate.ShellId,
            candidate.DesiredSettings,
            candidate.CatalogSnapshot,
            candidate.CandidateContext!,
            candidate.MissingFeatures);

        if (publishLifecycleNotifications)
        {
            await _notificationPublisher.PublishAsync(new ShellActivated(candidate.CandidateContext!), strategy: null, cancellationToken).ConfigureAwait(false);
        }

        if (commit.PreviousContext is not null && !ReferenceEquals(commit.PreviousContext, candidate.CandidateContext))
        {
            await DisposeShellContextAsync(commit.PreviousContext).ConfigureAwait(false);
        }
    }

    internal async Task RemoveAppliedRuntimeAsync(
        ShellId shellId,
        bool removeDesiredState = false,
        bool publishLifecycleNotifications = true,
        CancellationToken cancellationToken = default)
    {
        var result = removeDesiredState
            ? _runtimeStateStore.RemoveShell(shellId)
            : _runtimeStateStore.ClearAppliedRuntime(shellId);

        if (result.PreviousContext is not null && publishLifecycleNotifications)
        {
            await _notificationPublisher.PublishAsync(new ShellDeactivating(result.PreviousContext), strategy: null, cancellationToken).ConfigureAwait(false);
        }

        if (result.PreviousContext is not null)
        {
            await DisposeShellContextAsync(result.PreviousContext).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves feature dependencies and returns an ordered list of features for the shell.
    /// </summary>
    private List<string> ResolveFeatureDependencies(ShellSettings settings, IReadOnlyDictionary<string, ShellFeatureDescriptor> featureMap)
    {
        try
        {
            return _dependencyResolver.GetOrderedFeatures(settings.EnabledFeatures, featureMap);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to resolve feature dependencies for shell '{ShellId}'", settings.Id);
            throw;
        }
    }

    /// <summary>
    /// Creates a shell context with its service provider and configured features.
    /// Uses the holder pattern to allow ShellContext to be resolved from DI.
    /// </summary>
    private ShellContext CreateShellContext(
        ShellSettings settings,
        List<string> orderedFeatures,
        IReadOnlyCollection<ShellFeatureDescriptor> featureDescriptors,
        IReadOnlyCollection<string>? missingFeatures = null)
    {
        var contextHolder = new ShellContextHolder();
        var serviceProvider = BuildServiceProvider(settings, orderedFeatures, contextHolder, featureDescriptors);
        var context = new ShellContext(settings, serviceProvider, orderedFeatures.AsReadOnly(), missingFeatures);

        // Populate the holder so ShellContext can be resolved from DI
        contextHolder.Context = context;

        return context;
    }

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> for a shell with the specified features.
    /// </summary>
    /// <param name="settings">The shell settings.</param>
    /// <param name="orderedFeatures">The ordered list of features to configure.</param>
    /// <param name="contextHolder">A holder that will be populated with the ShellContext after the service provider is built.</param>
    /// <remarks>
    /// <para>
    /// The service provider is built by:
    /// </para>
    /// <list type="number">
    ///   <item><description>Creating a fresh <see cref="ServiceCollection"/> for the shell.</description></item>
    ///   <item><description>Copying all <see cref="ServiceDescriptor"/> entries from the root <see cref="IServiceCollection"/>.</description></item>
    ///   <item><description>Adding shell-specific core services (ShellSettings, ShellId, ShellContext).</description></item>
    ///   <item><description>Invoking <see cref="IShellFeature.ConfigureServices"/> for each enabled feature in dependency order.</description></item>
    ///   <item><description>Building the <see cref="IServiceProvider"/> only after all registrations are complete.</description></item>
    /// </list>
    /// <para>
    /// Because root services are added first and shell-specific services are added after,
    /// the DI container's "last registration wins" semantics ensure that shell-specific
    /// registrations override root registrations for the same service type.
    /// </para>
    /// </remarks>
    private IServiceProvider BuildServiceProvider(
        ShellSettings settings,
        List<string> orderedFeatures,
        ShellContextHolder contextHolder,
        IReadOnlyCollection<ShellFeatureDescriptor> featureDescriptors)
    {
        var shellServices = new ServiceCollection();

        // Step 1: Copy all root service registrations to the shell's service collection.
        // This enables inheritance of root services in shells.
        CopyRootServices(shellServices);

        // Step 2: Register shell-specific core services (ShellSettings, ShellId, ShellContext, IConfiguration).
        // These are added after root services, so they override any root registrations.
        RegisterCoreServices(shellServices, settings, contextHolder, _rootProvider, featureDescriptors);

        // Step 3: Configure feature services in dependency order.
        // Features can override root services by registering the same service type.
        // A single ShellFeatureContext is shared across all features so they can
        // exchange data through its Properties bag during construction.
        var featureContext = new ShellFeatureContext(settings, featureDescriptors);
        var postConfigureFeatures = new List<IPostConfigureShellServices>();
        if (orderedFeatures.Count > 0)
        {
            ConfigureFeatureServices(shellServices, orderedFeatures, settings, featureContext, postConfigureFeatures, featureDescriptors.ToDictionary(descriptor => descriptor.Id, descriptor => descriptor, StringComparer.OrdinalIgnoreCase));
        }

        // Step 3b: Post-configure — called after ALL features have registered their services
        // but before the container is built. Allows features to finalize registrations that
        // depend on what later-running features may have contributed (e.g. AddMassTransit).
        foreach (var feature in postConfigureFeatures)
        {
            feature.PostConfigureServices(shellServices);
        }

        // Step 4: Build the service provider only after all registrations are complete.
        // No temporary providers are created during feature configuration.
        return shellServices.BuildServiceProvider();
    }

    /// <summary>
    /// Copies all service descriptors from the root <see cref="IServiceCollection"/> to the shell's service collection,
    /// excluding CShell infrastructure types that should not be inherited by shells.
    /// </summary>
    /// <param name="shellServices">The shell's service collection to copy to.</param>
    /// <remarks>
    /// <para>
    /// This enables inheritance of root services in shells. Because these registrations are added first,
    /// shell-specific registrations added later will override them via "last registration wins" semantics.
    /// </para>
    /// <para>
    /// CShell infrastructure types (IShellHost, IShellContextScopeFactory, etc.) are excluded from copying
    /// to prevent shells from resolving a new DefaultShellHost using the shell provider as the "root,"
    /// which would break the documented semantics and fragment the shell cache.
    /// </para>
    /// <para>
    /// Performance optimization: The filtered root service descriptors are cached on first access to avoid
    /// re-enumerating and filtering the root IServiceCollection for each shell.
    /// </para>
    /// <para>
    /// Thread safety: This method is called within BuildServiceProvider, which is invoked inside
    /// the _buildLock in BuildShellContextInternal, so the caching is thread-safe.
    /// </para>
    /// </remarks>
    private void CopyRootServices(ServiceCollection shellServices)
    {
        // Cache the filtered root descriptors on first access for efficient bulk-copy to subsequent shells.
        // This avoids repeatedly enumerating and filtering the root IServiceCollection.
        // Thread-safe: This method is always called under _buildLock (see BuildShellContextInternal).
        _cachedRootDescriptors ??= _rootServices
            .Where(d => !_exclusionRegistry.ExcludedTypes.Contains(d.ServiceType))
            .ToList();

        // Bulk-copy cached descriptors to the shell's service collection
        foreach (var descriptor in _cachedRootDescriptors)
        {
            shellServices.Add(descriptor);
        }

        _logger.LogDebug("Copied {Count} service descriptors from root service collection (excluded {ExcludedCount} infrastructure types)",
            _cachedRootDescriptors.Count,
            _exclusionRegistry.ExcludedTypes.Count);
    }

    /// <summary>
    /// Registers core services required by all shells.
    /// </summary>
    private static void RegisterCoreServices(
        ServiceCollection services,
        ShellSettings settings,
        ShellContextHolder contextHolder,
        IServiceProvider rootProvider,
        IEnumerable<ShellFeatureDescriptor> allFeatureDescriptors)
    {
        // Register shell settings and shell ID for convenience
        services.AddSingleton(settings);
        // ShellId is a value type, so we register it directly as a singleton instance
        // rather than through AddSingleton<T>() which requires a reference type
        services.Add(ServiceDescriptor.Singleton(typeof(ShellId), settings.Id));

        // Add logging services so shell containers work with ASP.NET Core infrastructure
        services.AddLogging();

        // Register shell-scoped IConfiguration that merges shell-specific settings with root configuration
        // This allows features to use IConfiguration and IOptions<T> patterns with shell-specific values
        services.AddSingleton<ShellConfiguration>(_ =>
        {
            var rootConfiguration = rootProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            return new(settings, rootConfiguration);
        });
        
        // Register shell-scoped IConfiguration that merges shell-specific settings with root configuration
        // This allows features to use IConfiguration and IOptions<T> patterns with shell-specific values
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(sp => sp.GetRequiredService<ShellConfiguration>());

        // Register all discovered feature descriptors so features can query them.
        // Registered as both IReadOnlyCollection<> and IEnumerable<> for maximum flexibility.
        // Both registrations point to the same instance for efficiency.
        var featureDescriptorsList = allFeatureDescriptors.ToList().AsReadOnly();
        services.AddSingleton<IReadOnlyCollection<ShellFeatureDescriptor>>(featureDescriptorsList);
        services.AddSingleton<IEnumerable<ShellFeatureDescriptor>>(featureDescriptorsList);

        // Register the ShellContext using the holder pattern
        // The holder will be populated after the service provider is built
        services.AddSingleton<ShellContext>(sp => contextHolder.Context
            ?? throw new InvalidOperationException($"ShellContext for shell '{settings.Id}' has not been initialized yet. This may indicate a service is being resolved during shell construction."));

        // IShellManager is a root-level infrastructure service. Its default implementation
        // (DefaultShellManager) depends on IShellHost, which is excluded from shell containers.
        // Re-register it here as a delegation to the root provider so that shell code
        // (e.g. endpoints) can inject IShellManager and receive the root singleton.
        services.AddSingleton<Management.IShellManager>(sp => rootProvider.GetRequiredService<Management.IShellManager>());
    }

    /// <summary>
    /// Configures services from each feature's startup in dependency order.
    /// </summary>
    /// <remarks>
    /// Features are instantiated using the root IServiceProvider plus ShellSettings
    /// as an explicit parameter. No temporary shell ServiceProviders are created during configuration.
    /// This ensures features can only depend on root-level services in their constructors.
    /// </remarks>
    private void ConfigureFeatureServices(
        ServiceCollection services,
        List<string> orderedFeatures,
        ShellSettings settings,
        ShellFeatureContext featureContext,
        List<IPostConfigureShellServices> postConfigureFeatures,
        IReadOnlyDictionary<string, ShellFeatureDescriptor> featureMap)
    {
        var featuresWithStartups = orderedFeatures
            .Select(name => (Name: name, Descriptor: featureMap[name]))
            .Where(f => f.Descriptor.StartupType != null);

        foreach (var (featureName, descriptor) in featuresWithStartups)
        {
            ConfigureFeature(services, settings, featureName, descriptor, featureContext, postConfigureFeatures);
        }
    }

    private void ConfigureFeature(ServiceCollection services, ShellSettings settings, string featureName, ShellFeatureDescriptor descriptor, ShellFeatureContext featureContext, List<IPostConfigureShellServices> postConfigureFeatures)
    {
        try
        {
            // Create the feature instance using the root provider with ShellSettings as explicit parameter.
            // This ensures features can only depend on root-level services and ShellSettings, not shell services.
            var startup = CreateFeatureInstance(descriptor.StartupType!, settings, featureContext);

            // Step 1: Bind properties from shell configuration (appsettings / ConfigurationData).
            ApplyFeatureConfiguration(startup, settings, featureName);

            // Step 2: Apply any code-first configurator registered via WithFeature<T>(Action<T>).
            // This runs AFTER config binding so code-first values always win over appsettings.
            if (settings.FeatureConfigurators.TryGetValue(featureName, out var configurator))
            {
                configurator(startup);
                _logger.LogDebug("Applied code-first configurator to feature '{FeatureName}'", featureName);
            }

            startup.ConfigureServices(services);

            if (startup is IPostConfigureShellServices postConfigure)
                postConfigureFeatures.Add(postConfigure);

            _logger.LogDebug("Configured services from feature '{FeatureName}' startup type '{StartupType}'",
                featureName, descriptor.StartupType!.FullName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure services for feature '{FeatureName}'", featureName);
            throw new InvalidOperationException(
                $"Failed to configure services for feature '{featureName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Applies configuration to a feature from the shell configuration.
    /// This includes auto-binding feature properties and invoking IConfigurableFeature&lt;T&gt; Configure methods.
    /// </summary>
    private void ApplyFeatureConfiguration(IShellFeature feature, ShellSettings settings, string featureName)
    {
        try
        {
            // Get root configuration
            var rootConfiguration = _rootProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

            // Create shell-specific configuration
            var shellConfiguration = new ShellConfiguration(settings, rootConfiguration);

            // Get or create validator
            var validator = _rootProvider.GetService<IFeatureConfigurationValidator>()
                           ?? new DataAnnotationsFeatureConfigurationValidator();

            // Create configuration binder
            var binder = new FeatureConfigurationBinder(
                shellConfiguration,
                validator,
                _rootProvider.GetService<ILogger<FeatureConfigurationBinder>>());

            // Bind and configure
            binder.BindAndConfigure(feature, featureName);

            _logger.LogDebug("Applied configuration to feature '{FeatureName}'", featureName);
        }
        catch (FeatureConfigurationValidationException ex)
        {
            _logger.LogError(ex, "Configuration validation failed for feature '{FeatureName}'", featureName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply configuration to feature '{FeatureName}'. Feature will use default configuration.", featureName);
            // Don't throw - allow feature to use default configuration
        }
    }

    /// <summary>
    /// Creates an instance of the specified feature type using the feature factory.
    /// </summary>
    /// <param name="featureType">The feature type to instantiate.</param>
    /// <param name="shellSettings">The shell settings to pass as an explicit parameter.</param>
    /// <param name="featureContext">The shared context for this shell build, passed to all features.</param>
    /// <returns>The instantiated feature.</returns>
    /// <remarks>
    /// Uses the <see cref="IShellFeatureFactory"/> to instantiate the feature with proper
    /// dependency injection and automatic ShellSettings or ShellFeatureContext parameter handling.
    /// </remarks>
    private IShellFeature CreateFeatureInstance(Type featureType, ShellSettings shellSettings, ShellFeatureContext featureContext)
    {

        // The factory will automatically choose the right parameter based on what the feature constructor accepts:
        // - ShellFeatureContext (if available)
        // - ShellSettings (if available)
        // - No special parameters
        return _featureFactory.CreateFeature<IShellFeature>(featureType, shellSettings, featureContext);
    }

    /// <inheritdoc />
    public async ValueTask EvictShellAsync(ShellId shellId)
    {
        var context = _runtimeStateStore.EvictAppliedContext(shellId);
        if (context is not null)
        {
            _logger.LogDebug("Evicting cached shell context for '{ShellId}'", shellId);
            await DisposeShellContextAsync(context).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask EvictAllShellsAsync()
    {
        var contexts = _runtimeStateStore.EvictAllAppliedContexts();

        foreach (var context in contexts)
        {
            _logger.LogDebug("Evicting cached shell context for '{ShellId}'", context.Settings.Id);
            await DisposeShellContextAsync(context).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeShellContextAsync(ShellContext context)
    {
        if (context.ServiceProvider is IAsyncDisposable asyncDisposable)
        {
            try
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing service provider for shell '{ShellId}'", context.Settings.Id);
            }
        }
        else if (context.ServiceProvider is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing service provider for shell '{ShellId}'", context.Settings.Id);
            }
        }
    }

    /// <summary>
    /// A holder class that allows the ShellContext to be set after the service provider is built.
    /// This is an internal implementation detail and is not registered in the service collection.
    /// </summary>
    private sealed class ShellContextHolder
    {
        public ShellContext? Context { get; set; }
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if this instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var context in _runtimeStateStore.EvictAllAppliedContexts())
        {
            await DisposeShellContextAsync(context).ConfigureAwait(false);
        }

        _disposed = true;

        GC.SuppressFinalize(this);
    }

    private void SeedDesiredState(IEnumerable<ShellSettings> settings)
    {
        foreach (var shell in settings)
        {
            _runtimeStateStore.RecordDesired(shell);
        }
    }

    private void InitializeAppliedRuntimes(IEnumerable<ShellSettings> settings)
    {
        var snapshot = _runtimeFeatureCatalog.CurrentSnapshot;

        foreach (var shell in settings)
        {
            var record = _runtimeStateStore.Get(shell.Id) ?? _runtimeStateStore.RecordDesired(shell);
            var candidate = BuildCandidate(record, snapshot);

            if (candidate.IsReadyToCommit)
            {
                _runtimeStateStore.CommitAppliedRuntime(candidate.ShellId, candidate.DesiredSettings, candidate.CatalogSnapshot, candidate.CandidateContext!, candidate.MissingFeatures);
                continue;
            }

            _runtimeStateStore.MarkFailed(candidate.ShellId, candidate.FailureReason);
        }
    }
}
