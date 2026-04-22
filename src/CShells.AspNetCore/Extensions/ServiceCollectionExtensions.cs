using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Middleware;
using CShells.DependencyInjection;
using CShells.Hosting;
using CShells.Lifecycle;
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
    /// Adds CShells ASP.NET Core integration services. Registers sensible defaults:
    /// web routing resolver (path + host-based), endpoint routing, the shell resolver
    /// orchestrator, and HTTP context accessor.
    /// </summary>
    public static CShellsBuilder AddCShellsAspNetCore(
        this IServiceCollection services,
        Action<CShellsBuilder>? configure = null)
    {
        Guard.Against.Null(services);

        var builder = services.AddCShells(configure);

        services.TryAddSingleton<ShellResolverOptions>();
        services.TryAddSingleton<IShellResolver, DefaultShellResolver>();
        services.AddMemoryCache();
        services.AddOptions<ShellMiddlewareOptions>();

        var pipelineWasConfigured = services.Any(d => d.ServiceType == typeof(ResolverPipelineConfigurationMarker));
        if (!pipelineWasConfigured)
        {
            services.TryAddSingleton(new Resolution.WebRoutingShellResolverOptions());

            services.AddSingleton<IShellResolverStrategy>(sp => new Resolution.WebRoutingShellResolver(
                sp.GetRequiredService<IShellRegistry>(),
                sp.GetRequiredService<Resolution.WebRoutingShellResolverOptions>()));
            services.AddSingleton<IShellResolverStrategy, DefaultShellResolverStrategy>();

            services.Configure<ShellResolverOptions>(opt =>
            {
                opt.SetOrder<Resolution.WebRoutingShellResolver>(0);
                opt.SetOrder<DefaultShellResolverStrategy>(1000);
            });
        }

        builder.WithEndpointRouting();

        services.AddSingleton<IShellServiceExclusionProvider, Hosting.AspNetCoreShellServiceExclusionProvider>();
        services.AddHttpContextAccessor();

        return builder;
    }
}
