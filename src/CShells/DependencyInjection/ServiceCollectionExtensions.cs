using CShells.Features;
using CShells.Hosting;
using CShells.Lifecycle;
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

        // Register the root service collection accessor so the provider builder can copy root
        // service registrations into each shell's service collection.
        services.TryAddSingleton<IRootServiceCollectionAccessor>(
            _ => new RootServiceCollectionAccessor(services));

        // Core infrastructure.
        services.AddSingleton<IShellServiceExclusionProvider, DefaultShellServiceExclusionProvider>();
        services.TryAddSingleton<IShellServiceExclusionRegistry, ShellServiceExclusionRegistry>();
        services.TryAddSingleton<IShellFeatureFactory, DefaultShellFeatureFactory>();

        var builder = new CShellsBuilder(services);

        services.TryAddSingleton<RuntimeFeatureCatalog>(sp =>
        {
            var logger = sp.GetService<ILogger<RuntimeFeatureCatalog>>();
            return new RuntimeFeatureCatalog(ct => builder.BuildFeatureAssembliesAsync(sp, ct), logger);
        });

        // Lifecycle surface: provider builder + registry + auto-logger + drain defaults + startup service.
        services.TryAddSingleton<ShellProviderBuilder>(sp => new ShellProviderBuilder(
            sp.GetRequiredService<IRootServiceCollectionAccessor>(),
            sp,
            sp.GetRequiredService<IShellServiceExclusionRegistry>(),
            sp.GetRequiredService<IShellFeatureFactory>(),
            sp.GetRequiredService<RuntimeFeatureCatalog>(),
            sp.GetService<ILogger<ShellProviderBuilder>>()));

        services.TryAddSingleton<ShellRegistry>(sp => new ShellRegistry(
            sp.GetServices<IShellBlueprint>(),
            sp.GetRequiredService<ShellProviderBuilder>(),
            sp,
            sp.GetService<ILogger<ShellRegistry>>(),
            sp.GetServices<IShellLifecycleSubscriber>()));
        services.TryAddSingleton<IShellRegistry>(sp => sp.GetRequiredService<ShellRegistry>());

        services.TryAddSingleton<IDrainPolicy>(_ => new Lifecycle.Policies.FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30)));
        services.TryAddSingleton(DrainGracePeriod.Default);

        services.AddSingleton<ShellLifecycleLogger>();
        services.AddSingleton<IShellLifecycleSubscriber>(sp => sp.GetRequiredService<ShellLifecycleLogger>());

        services.AddHostedService<CShellsStartupHostedService>();

        configure?.Invoke(builder);

        return builder;
    }
}
