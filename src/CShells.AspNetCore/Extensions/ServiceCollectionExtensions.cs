using CShells.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CShells.AspNetCore.Extensions;

/// <summary>
/// Extension methods for configuring CShells ASP.NET Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds CShells ASP.NET Core integration services to the service collection.
    /// Registers the <see cref="IShellResolver"/> orchestrator and a default fallback strategy
    /// if no custom strategies have been registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCShellsAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the default orchestrator (only if not already registered)
        services.TryAddSingleton<IShellResolver, DefaultShellResolver>();

        // Register default fallback strategy (only if no strategies are registered)
        // This uses TryAddEnumerable to avoid duplicates
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellResolverStrategy, DefaultShellResolverStrategy>());

        return services;
    }
}
