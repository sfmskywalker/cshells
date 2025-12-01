using CShells.AspNetCore.Middleware;
using CShells.AspNetCore.Routing;
using CShells.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.AspNetCore.Extensions;

/// <summary>
/// Extension methods for configuring CShells middleware and endpoint routing.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds CShells endpoint routing to the application pipeline.
    /// This must be called after UseRouting() and before UseEndpoints().
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures dynamic endpoint routing for multi-tenant shell applications.
    /// Shells can be loaded at startup from configuration or asynchronously from storage,
    /// and can be added, removed, or updated at runtime without restarting the application.
    /// </para>
    /// <para>
    /// Proper usage:
    /// <code>
    /// app.UseRouting();
    /// app.UseCShells();
    /// app.UseEndpoints(endpoints => { endpoints.MapCShells(); });
    /// </code>
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseCShells(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.ApplicationServices.GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(ApplicationBuilderExtensions))
            ?? NullLogger.Instance;

        logger.LogInformation("Configuring CShells endpoint routing");

        // Add shell resolution middleware (sets current shell context on request)
        app.UseMiddleware<ShellMiddleware>();

        logger.LogInformation("CShells endpoint routing configured successfully");

        return app;
    }

    /// <summary>
    /// Configures CShells endpoints. This should be called within UseEndpoints().
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint convention builder.</returns>
    /// <example>
    /// <code>
    /// app.UseEndpoints(endpoints =>
    /// {
    ///     endpoints.MapCShells();
    ///     endpoints.MapControllers();
    /// });
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapCShells(this IEndpointRouteBuilder endpoints)
    {
        var logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(ApplicationBuilderExtensions))
            ?? NullLogger.Instance;

        logger.LogInformation("Mapping CShells endpoints");

        // Capture the endpoint route builder in the accessor so notification handlers can use it
        var accessor = endpoints.ServiceProvider.GetRequiredService<EndpointRouteBuilderAccessor>();
        accessor.EndpointRouteBuilder = endpoints;

        // Get the dynamic endpoint data source
        var endpointDataSource = endpoints.ServiceProvider.GetRequiredService<DynamicShellEndpointDataSource>();

        // Add the data source to the endpoint route builder
        // This makes all shell endpoints available to the routing system
        endpoints.DataSources.Add(endpointDataSource);

        logger.LogInformation("CShells endpoints mapped successfully");

        // Return a convention builder (even though we don't have specific conventions to apply)
        return new EndpointConventionBuilder(endpointDataSource);
    }

    /// <summary>
    /// A simple endpoint convention builder for shell endpoints.
    /// </summary>
    private class EndpointConventionBuilder(DynamicShellEndpointDataSource dataSource) : IEndpointConventionBuilder
    {
        private readonly DynamicShellEndpointDataSource _dataSource = dataSource;

        public void Add(Action<EndpointBuilder> convention)
        {
            // Conventions can be applied to all endpoints in the data source
            // For now, we don't need to support this, but it's here for extensibility
        }
    }
}
