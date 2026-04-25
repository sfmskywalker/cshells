using CShells.Features;
using CShells.Hosting;
using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
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

        // Blueprint providers: code-seeded blueprints from AddShell(...) feed into the built-in
        // InMemoryShellBlueprintProvider. Additional providers are registered via
        // CShellsBuilder.AddBlueprintProvider (used by configuration + FluentStorage providers).
        // The composite multiplexes all of them in DI-registration order for lookup precedence.
        services.TryAddSingleton<InMemoryShellBlueprintProvider>(_ =>
            new InMemoryShellBlueprintProvider(builder.InlineBlueprints));

        services.TryAddSingleton<CompositeShellBlueprintProvider>(sp =>
        {
            var providers = new List<IShellBlueprintProvider>
            {
                sp.GetRequiredService<InMemoryShellBlueprintProvider>()
            };
            foreach (var factory in builder.ProviderFactories)
                providers.Add(factory(sp));
            return new CompositeShellBlueprintProvider(providers);
        });

        // External callers resolving IShellBlueprintProvider get the composite view (the
        // aggregate of every source). Registry depends on the concrete composite directly for
        // type safety and to avoid ambiguity with sub-providers registered as IShellBlueprintProvider.
        services.TryAddSingleton<IShellBlueprintProvider>(sp => sp.GetRequiredService<CompositeShellBlueprintProvider>());

        services.TryAddSingleton<ShellRegistry>(sp => new ShellRegistry(
            sp.GetRequiredService<CompositeShellBlueprintProvider>(),
            sp.GetRequiredService<ShellProviderBuilder>(),
            sp,
            sp.GetService<ILogger<ShellRegistry>>(),
            sp.GetServices<IShellLifecycleSubscriber>()));
        services.TryAddSingleton<IShellRegistry>(sp => sp.GetRequiredService<ShellRegistry>());

        services.TryAddSingleton<IDrainPolicy>(_ => new Lifecycle.Policies.FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30)));
        services.TryAddSingleton(DrainGracePeriod.Default);

        services.AddSingleton<ShellLifecycleLogger>();
        services.AddSingleton<IShellLifecycleSubscriber>(sp => sp.GetRequiredService<ShellLifecycleLogger>());

        // Pre-warm list singleton, populated from the builder. Consumed by the startup hosted service.
        services.TryAddSingleton<PreWarmShellList>(_ => new PreWarmShellList(builder.PreWarmNames));
        services.AddHostedService<CShellsStartupHostedService>();

        configure?.Invoke(builder);

        return builder;
    }
}
