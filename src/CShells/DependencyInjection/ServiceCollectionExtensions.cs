using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Lifecycle;
using CShells.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CShells.DependencyInjection;

/// <summary>
/// ServiceCollection extensions for registering CShells.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CShells services and returns a builder for further configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action to customize the CShells builder.</param>
    /// <returns>A CShells builder for further configuration.</returns>
    public static CShellsBuilder AddCShells(
        this IServiceCollection services,
        Action<CShellsBuilder>? configure = null)
    {
        Guard.Against.Null(services);

        // Register the root service collection accessor as early as possible.
        // This allows the shell host to copy root service registrations into each shell's service collection.
        // Note: The captured 'services' reference remains valid for the lifetime of the application.
        // Because IServiceCollection is mutable, any services added after AddCShells but before shells are built
        // will still be inherited by shells. This subtle behavior is correct but worth documenting for future maintainers.
        services.TryAddSingleton<IRootServiceCollectionAccessor>(
            _ => new RootServiceCollectionAccessor(services));

        // Register the default exclusion provider for core CShells infrastructure types
        services.AddSingleton<IShellServiceExclusionProvider, DefaultShellServiceExclusionProvider>();

        // Register the service exclusion registry (aggregates all providers)
        services.TryAddSingleton<Hosting.IShellServiceExclusionRegistry, Hosting.ShellServiceExclusionRegistry>();

        // Register the feature factory for consistent feature instantiation across the framework
        services.TryAddSingleton<IShellFeatureFactory, DefaultShellFeatureFactory>();

        // Register the notification publisher for shell lifecycle events
        services.TryAddSingleton<Notifications.INotificationPublisher, Notifications.DefaultNotificationPublisher>();

        // Register notification handlers for shell lifecycle events
        services.TryAddSingleton<Notifications.INotificationHandler<Notifications.ShellActivated>, Notifications.ShellActivationHandler>();
        services.TryAddSingleton<Notifications.INotificationHandler<Notifications.ShellDeactivating>, Notifications.ShellDeactivationHandler>();

        // Register the shell settings cache
        var cache = new ShellSettingsCache();
        services.TryAddSingleton<ShellSettingsCache>(cache);
        services.TryAddSingleton<IShellSettingsCache>(cache);

        services.TryAddSingleton<ShellRuntimeStateStore>();
        services.TryAddSingleton<ShellRuntimeStateAccessor>();
        services.TryAddSingleton<IShellRuntimeStateAccessor>(sp => sp.GetRequiredService<ShellRuntimeStateAccessor>());

        var builder = new CShellsBuilder(services);

        services.TryAddSingleton<RuntimeFeatureCatalog>(sp =>
        {
            var logger = sp.GetService<ILogger<RuntimeFeatureCatalog>>();
            return new RuntimeFeatureCatalog(ct => builder.BuildFeatureAssembliesAsync(sp, ct), logger);
        });

        // Register IShellHost using the DefaultShellHost.
        // The root IServiceProvider is passed to allow IShellFeature constructors to resolve root-level services.
        // The root IServiceCollection is passed via the accessor to enable service inheritance in shells.
        // The cache is passed directly, and DefaultShellHost will call GetAll() at runtime.
        //
        // Note: The cache may be empty when IShellHost is constructed. This is OK - shells can be
        // loaded later via the hosted service or dynamically at runtime via the cache.
        services.AddSingleton<DefaultShellHost>(sp =>
        {
            var shellCache = sp.GetRequiredService<ShellSettingsCache>();
            var logger = sp.GetService<ILogger<DefaultShellHost>>();
            var rootServicesAccessor = sp.GetRequiredService<IRootServiceCollectionAccessor>();
            var featureFactory = sp.GetRequiredService<IShellFeatureFactory>();
            var exclusionRegistry = sp.GetRequiredService<Hosting.IShellServiceExclusionRegistry>();
            var runtimeFeatureCatalog = sp.GetRequiredService<RuntimeFeatureCatalog>();
            var runtimeStateStore = sp.GetRequiredService<ShellRuntimeStateStore>();
            var notificationPublisher = sp.GetRequiredService<Notifications.INotificationPublisher>();

            return new DefaultShellHost(shellCache, ct => builder.BuildFeatureAssembliesAsync(sp, ct), rootProvider: sp, rootServicesAccessor, featureFactory, exclusionRegistry, seedDesiredStateFromCache: true, runtimeFeatureCatalog, runtimeStateStore, notificationPublisher, logger);
        });
        services.AddSingleton<IShellHost>(sp => sp.GetRequiredService<DefaultShellHost>());
        services.AddSingleton<IShellHostInitializer>(sp => sp.GetRequiredService<DefaultShellHost>());

        // Register the default shell context scope factory.
        services.AddSingleton<IShellContextScopeFactory, DefaultShellContextScopeFactory>();

        // Register the shell manager for runtime shell lifecycle management
        services.TryAddSingleton<DefaultShellManager>(sp => new DefaultShellManager(
            sp.GetRequiredService<DefaultShellHost>(),
            sp.GetRequiredService<ShellSettingsCache>(),
            sp.GetRequiredService<IShellSettingsProvider>(),
            sp.GetRequiredService<Notifications.INotificationPublisher>(),
            sp.GetService<ILogger<DefaultShellManager>>()));
        services.TryAddSingleton<IShellManager>(sp => sp.GetRequiredService<DefaultShellManager>());

        // Register hosted services for feature discovery and shell lifecycle coordination.
        services.AddHostedService<ShellFeatureInitializationHostedService>();
        services.AddHostedService<ShellSettingsCacheInitializer>();
        services.AddHostedService<ShellStartupHostedService>();
        
        // Register the composite shell settings provider factory immediately
        // This must be done BEFORE configure is called so that DefaultShellManager can be constructed
        services.TryAddSingleton<IShellSettingsProvider>(sp =>
        {
            var providers = new List<IShellSettingsProvider>();

            // Add code-first shells provider if any shells were defined
            if (builder.CodeFirstShells.Count > 0)
            {
                providers.Add(new InMemoryShellSettingsProvider(builder.CodeFirstShells));
            }

            // Build and add all registered providers
            var registeredProviders = builder.BuildProviders(sp);
            providers.AddRange(registeredProviders);

            IShellSettingsProvider provider;

            // If no providers were registered, return an empty provider
            if (providers.Count == 0)
                provider = new InMemoryShellSettingsProvider([]);
            else if (providers.Count == 1)
                provider = providers[0];
            else
                provider = new CompositeShellSettingsProvider(providers);

            // Wrap with ConfigureAllShells configurators when registered
            if (builder.ShellConfigurators.Count > 0)
                provider = new ConfiguringShellSettingsProvider(provider, builder.ShellConfigurators);

            return provider;
        });
        
        // ==================================================================================
        // Lifecycle (new API — runs alongside legacy until Phase 15 deletes the legacy surface).
        // Registers the generation-based `IShellRegistry`, the provider builder, and the
        // auto-subscribed structured logging subscriber. Blueprints registered via
        // `CShellsBuilder.AddShell(name, ...)` are resolved as `IEnumerable<IShellBlueprint>`
        // at registry-construction time.
        // ==================================================================================
        services.TryAddSingleton<ShellProviderBuilder>(sp => new ShellProviderBuilder(
            sp.GetRequiredService<IRootServiceCollectionAccessor>(),
            sp,
            sp.GetRequiredService<Hosting.IShellServiceExclusionRegistry>(),
            sp.GetRequiredService<IShellFeatureFactory>(),
            sp.GetRequiredService<RuntimeFeatureCatalog>(),
            sp.GetService<ILogger<ShellProviderBuilder>>()));

        services.TryAddSingleton<ShellRegistry>(sp => new ShellRegistry(
            sp.GetServices<IShellBlueprint>(),
            sp.GetRequiredService<ShellProviderBuilder>(),
            sp,
            sp.GetService<ILogger<ShellRegistry>>()));
        services.TryAddSingleton<IShellRegistry>(sp => sp.GetRequiredService<ShellRegistry>());

        // Drain defaults. Hosts override via ConfigureDrainPolicy / ConfigureGracePeriod.
        services.TryAddSingleton<IDrainPolicy>(_ => new Lifecycle.Policies.FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30)));
        services.TryAddSingleton(DrainGracePeriod.Default);

        services.AddSingleton<ShellLifecycleLogger>();
        // Eager resolution: materialising the logger as an `IShellLifecycleSubscriber`
        // registration ensures it is constructed (and thus subscribes itself) as part of the
        // container's singleton graph whenever the registry is resolved.
        services.AddSingleton<IShellLifecycleSubscriber>(sp => sp.GetRequiredService<ShellLifecycleLogger>());

        // Hosted service: activates every blueprint on startup and drains every active shell
        // on shutdown (FR-035, FR-036).
        services.AddHostedService<CShellsStartupHostedService>();

        configure?.Invoke(builder);

        return builder;
    }

}
