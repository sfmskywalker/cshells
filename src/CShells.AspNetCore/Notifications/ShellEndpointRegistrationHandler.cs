using CShells.AspNetCore.Features;
using CShells.AspNetCore.Routing;
using CShells.Features;
using CShells.Hosting;
using CShells.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.AspNetCore.Notifications;

/// <summary>
/// Handles shell lifecycle notifications by registering/removing endpoints and middleware in the dynamic endpoint data source.
/// </summary>
public class ShellEndpointRegistrationHandler :
    INotificationHandler<ShellActivated>,
    INotificationHandler<ShellDeactivating>,
    INotificationHandler<ShellRemoved>
{
    private readonly DynamicShellEndpointDataSource _endpointDataSource;
    private readonly EndpointRouteBuilderAccessor _endpointRouteBuilderAccessor;
    private readonly ApplicationBuilderAccessor _applicationBuilderAccessor;
    private readonly IShellFeatureFactory _featureFactory;
    private readonly IShellHost _shellHost;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<ShellEndpointRegistrationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellEndpointRegistrationHandler"/> class.
    /// </summary>
    public ShellEndpointRegistrationHandler(
        DynamicShellEndpointDataSource endpointDataSource,
        IShellFeatureFactory featureFactory,
        IShellHost shellHost,
        EndpointRouteBuilderAccessor endpointRouteBuilderAccessor,
        ApplicationBuilderAccessor applicationBuilderAccessor,
        IHostEnvironment? environment = null,
        ILogger<ShellEndpointRegistrationHandler>? logger = null)
    {
        _endpointDataSource = endpointDataSource;
        _endpointRouteBuilderAccessor = endpointRouteBuilderAccessor;
        _applicationBuilderAccessor = applicationBuilderAccessor;
        _featureFactory = featureFactory;
        _shellHost = shellHost;
        _environment = environment;
        _logger = logger ?? NullLogger<ShellEndpointRegistrationHandler>.Instance;
    }

    /// <inheritdoc />
    public Task HandleAsync(ShellActivated notification, CancellationToken cancellationToken = default)
    {
        if (_endpointRouteBuilderAccessor.EndpointRouteBuilder == null)
        {
            _logger.LogWarning("Cannot register endpoints for shell '{ShellId}': IEndpointRouteBuilder not available. " +
                               "Endpoints will be registered on next application start.", notification.Context.Id);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Registering endpoints for applied shell '{ShellId}'", notification.Context.Id);
        _endpointDataSource.RemoveEndpoints(notification.Context.Id);
        RegisterShellEndpoints(notification.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleAsync(ShellDeactivating notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing endpoints for deactivating shell '{ShellId}'", notification.Context.Id);
        _endpointDataSource.RemoveEndpoints(notification.Context.Id);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleAsync(ShellRemoved notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing endpoints for shell '{ShellId}'", notification.ShellId);
        _endpointDataSource.RemoveEndpoints(notification.ShellId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers endpoints for a single shell.
    /// </summary>
    private void RegisterShellEndpoints(ShellContext shellContext)
    {
        var endpointRouteBuilder = _endpointRouteBuilderAccessor.EndpointRouteBuilder;
        if (endpointRouteBuilder == null)
            return;

        var settings = shellContext.Settings;

        _logger.LogDebug("Registering endpoints for shell '{ShellId}'", settings.Id);

        // Get path prefix from shell configuration
        _logger.LogInformation("Shell '{ShellId}' has {ConfigCount} configuration entries", settings.Id, settings.ConfigurationData.Count);

        var shellPathPrefix = GetPathPrefix(settings);
        var routePrefix = GetRoutePrefix(settings);

        // Combine shell path prefix with route prefix (e.g., "/foo" + "api/v1" = "/foo/api/v1")
        var combinedPrefix = CombinePrefixes(shellPathPrefix, routePrefix);

        _logger.LogInformation("Shell '{ShellId}' path prefix: '{PathPrefix}', route prefix: '{RoutePrefix}', combined: '{Combined}'",
            settings.Id,
            shellPathPrefix ?? "(none)",
            routePrefix ?? "(none)",
            combinedPrefix ?? "(none)");


        // Create shell-scoped endpoint builder with the combined prefix
        // This ensures that endpoint mapping (e.g., FastEndpoints) can resolve shell-scoped services
        var shellEndpointBuilder = new ShellEndpointRouteBuilder(
            endpointRouteBuilder,
            settings.Id,
            settings,
            shellContext.ServiceProvider,
            combinedPrefix);

        // Get the already-discovered feature descriptors to create the context
        var allFeatureDescriptors = shellContext.ServiceProvider.GetRequiredService<IEnumerable<ShellFeatureDescriptor>>().ToList();
        var featureContext = new ShellFeatureContext(settings, allFeatureDescriptors.AsReadOnly());

        // Register middleware for shell features that implement IMiddlewareShellFeature
        RegisterShellMiddleware(settings, shellContext, allFeatureDescriptors, featureContext, shellPathPrefix);

        // Discover web features using the already-retrieved data
        var webFeatures = DiscoverWebFeatures(shellContext, allFeatureDescriptors);

        // Map endpoints for each web feature
        foreach (var (featureId, featureType) in webFeatures)
        {
            try
            {
                var feature = _featureFactory.CreateFeature<IWebShellFeature>(featureType, settings, featureContext);
                feature.MapEndpoints(shellEndpointBuilder, _environment);

                _logger.LogDebug("Mapped endpoints for feature '{FeatureId}' in shell '{ShellId}'",
                    featureId, settings.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to map endpoints for feature '{FeatureId}' in shell '{ShellId}'",
                    featureId, settings.Id);
                throw;
            }
        }

        // Add all endpoints to the data source
        var shellEndpoints = shellEndpointBuilder.GetEndpoints().ToList();

        // Log the actual route patterns being registered
        foreach (var endpoint in shellEndpoints)
        {
            if (endpoint is RouteEndpoint routeEndpoint)
            {
                _logger.LogInformation("Registering endpoint for shell '{ShellId}': {RoutePattern}",
                    settings.Id, routeEndpoint.RoutePattern.RawText);
            }
        }

        _endpointDataSource.AddEndpoints(shellEndpoints);

        _logger.LogDebug("Registered {Count} endpoint(s) for shell '{ShellId}'", shellEndpoints.Count, settings.Id);
    }

    /// <summary>
    /// Discovers web features that are enabled for a shell, including resolved dependencies.
    /// </summary>
    /// <remarks>
    /// This method uses the already-discovered feature descriptors passed from the caller
    /// instead of re-scanning assemblies, ensuring consistency and better performance.
    /// </remarks>
    private static IEnumerable<(string FeatureId, Type FeatureType)> DiscoverWebFeatures(
        ShellContext shellContext,
        IEnumerable<ShellFeatureDescriptor> allFeatureDescriptors)
    {
        var enabledFeatures = shellContext.EnabledFeatures;

        // Filter for web features that are enabled (including dependencies)
        return allFeatureDescriptors
            .Where(d => d.StartupType != null &&
                        typeof(IWebShellFeature).IsAssignableFrom(d.StartupType) &&
                        enabledFeatures.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
            .Select(d => (d.Id, d.StartupType!));
    }

    /// <summary>
    /// Discovers middleware features that are enabled for a shell, including resolved dependencies.
    /// </summary>
    private static IEnumerable<(string FeatureId, Type FeatureType)> DiscoverMiddlewareFeatures(
        ShellContext shellContext,
        IEnumerable<ShellFeatureDescriptor> allFeatureDescriptors)
    {
        var enabledFeatures = shellContext.EnabledFeatures;

        return allFeatureDescriptors
            .Where(d => d.StartupType != null &&
                        typeof(IMiddlewareShellFeature).IsAssignableFrom(d.StartupType) &&
                        enabledFeatures.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
            .Select(d => (d.Id, d.StartupType!));
    }

    /// <summary>
    /// Registers middleware components for shell features that implement <see cref="IMiddlewareShellFeature"/>.
    /// </summary>
    private void RegisterShellMiddleware(
        ShellSettings settings,
        ShellContext shellContext,
        IReadOnlyCollection<ShellFeatureDescriptor> allFeatureDescriptors,
        ShellFeatureContext featureContext,
        string? shellPathPrefix)
    {
        var appBuilder = _applicationBuilderAccessor.ApplicationBuilder;
        if (appBuilder == null)
        {
            _logger.LogDebug("IApplicationBuilder not available, skipping middleware registration for shell '{ShellId}'", settings.Id);
            return;
        }

        var middlewareFeatures = DiscoverMiddlewareFeatures(shellContext, allFeatureDescriptors).ToList();
        if (middlewareFeatures.Count == 0)
            return;

        _logger.LogInformation("Registering middleware for {Count} feature(s) in shell '{ShellId}'",
            middlewareFeatures.Count, settings.Id);

        foreach (var (featureId, featureType) in middlewareFeatures)
        {
            try
            {
                var feature = _featureFactory.CreateFeature<IMiddlewareShellFeature>(featureType, settings, featureContext);

                if (!string.IsNullOrEmpty(shellPathPrefix))
                {
                    // Scope the middleware to the shell's path prefix using Map()
                    appBuilder.Map(shellPathPrefix, branch =>
                    {
                        feature.UseMiddleware(branch, _environment);
                    });
                }
                else
                {
                    // No path prefix — register middleware at the root level
                    feature.UseMiddleware(appBuilder, _environment);
                }

                _logger.LogDebug("Registered middleware for feature '{FeatureId}' in shell '{ShellId}'",
                    featureId, settings.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register middleware for feature '{FeatureId}' in shell '{ShellId}'",
                    featureId, settings.Id);
                throw;
            }
        }
    }

    /// <summary>
    /// Gets the path prefix for a shell from its configuration.
    /// </summary>
    private static string? GetPathPrefix(ShellSettings settings)
    {
        // Read WebRouting:Path from ConfigurationData
        var path = settings.GetConfiguration("WebRouting:Path");

        // Null means no path routing configured for this shell
        if (path == null)
            return null;

        // Empty string is valid and means root path (no prefix)
        if (path == string.Empty)
            return null; // Return null for empty path to indicate no prefix

        var trimmedPath = path.Trim();
        if (!trimmedPath.StartsWith('/')) trimmedPath = "/" + trimmedPath;
        if (trimmedPath.EndsWith('/') && trimmedPath.Length > 1) trimmedPath = trimmedPath.TrimEnd('/');

        return trimmedPath;
    }

    /// <summary>
    /// Gets the route prefix for endpoints from shell configuration data.
    /// </summary>
    private static string? GetRoutePrefix(ShellSettings settings)
    {
        // Read from WebRouting:RoutePrefix in ConfigurationData
        const string routePrefixKey = "WebRouting:RoutePrefix";

        if (settings.ConfigurationData.TryGetValue(routePrefixKey, out var prefix) &&
            prefix != null)
        {
            var prefixStr = prefix.ToString();
            if (string.IsNullOrWhiteSpace(prefixStr))
                return null;

            var trimmedPrefix = prefixStr.Trim();

            // Ensure prefix doesn't start or end with '/'
            if (trimmedPrefix.StartsWith('/')) trimmedPrefix = trimmedPrefix.TrimStart('/');
            if (trimmedPrefix.EndsWith('/')) trimmedPrefix = trimmedPrefix.TrimEnd('/');

            return trimmedPrefix;
        }

        return null;
    }

    /// <summary>
    /// Combines the shell path prefix with a route prefix.
    /// </summary>
    /// <example>
    /// CombinePrefixes("/foo", "api/v1") => "/foo/api/v1"
    /// CombinePrefixes("/foo", null) => "/foo"
    /// CombinePrefixes(null, "api/v1") => "/api/v1"
    /// </example>
    private static string? CombinePrefixes(string? shellPathPrefix, string? routePrefix)
    {
        if (string.IsNullOrWhiteSpace(shellPathPrefix) && string.IsNullOrWhiteSpace(routePrefix))
            return null;

        if (string.IsNullOrWhiteSpace(shellPathPrefix))
            return "/" + routePrefix;

        if (string.IsNullOrWhiteSpace(routePrefix))
            return shellPathPrefix;

        return $"{shellPathPrefix}/{routePrefix}";
    }
}