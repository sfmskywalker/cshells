using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.DependencyInjection;

/// <summary>
/// Builder for configuring CShells services with a fluent API. Each registered shell becomes
/// an <see cref="IShellBlueprint"/> that the <see cref="IShellRegistry"/> resolves at activation
/// time.
/// </summary>
public class CShellsBuilder
{
    private readonly List<Func<IServiceProvider, IFeatureAssemblyProvider>> _featureAssemblyProviderRegistrations = [];
    private readonly List<Action<ShellBuilder>> _shellConfigurators = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CShellsBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CShellsBuilder(IServiceCollection services)
    {
        Services = Guard.Against.Null(services);
    }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Gets the configurators registered via <see cref="ConfigureAllShells"/>.
    /// </summary>
    internal IReadOnlyList<Action<ShellBuilder>> ShellConfigurators => _shellConfigurators.AsReadOnly();

    /// <summary>
    /// Gets a value indicating whether explicit feature assembly providers were configured.
    /// </summary>
    internal bool UsesExplicitFeatureAssemblyProviders => _featureAssemblyProviderRegistrations.Count > 0;

    /// <summary>
    /// Registers a feature assembly provider factory.
    /// </summary>
    internal void RegisterFeatureAssemblyProvider(Func<IServiceProvider, IFeatureAssemblyProvider> registration)
    {
        _featureAssemblyProviderRegistrations.Add(Guard.Against.Null(registration));
    }

    /// <summary>
    /// Builds all registered feature assembly providers and returns them in registration order.
    /// </summary>
    internal IReadOnlyList<IFeatureAssemblyProvider> BuildFeatureAssemblyProviders(IServiceProvider serviceProvider)
    {
        Guard.Against.Null(serviceProvider);

        var providers = new List<IFeatureAssemblyProvider>(_featureAssemblyProviderRegistrations.Count);

        foreach (var registration in _featureAssemblyProviderRegistrations)
        {
            var provider = registration(serviceProvider)
                ?? throw new InvalidOperationException("Feature assembly provider registrations must return a non-null provider instance.");

            providers.Add(provider);
        }

        return providers.AsReadOnly();
    }

    /// <summary>
    /// Resolves the assemblies that should be scanned for shell feature discovery.
    /// </summary>
    internal async Task<IReadOnlyCollection<Assembly>> BuildFeatureAssembliesAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(serviceProvider);

        return UsesExplicitFeatureAssemblyProviders
            ? await CShells.Features.FeatureAssemblyResolver.ResolveAssembliesAsync(BuildFeatureAssemblyProviders(serviceProvider), serviceProvider, cancellationToken)
            : CShells.Features.FeatureAssemblyResolver.ResolveHostAssemblies();
    }

    /// <summary>
    /// Registers a configurator applied to every shell — applied to every
    /// <see cref="ShellBuilder"/> that a <see cref="DelegateShellBlueprint"/> hands out during
    /// activation or reload.
    /// </summary>
    public CShellsBuilder ConfigureAllShells(Action<ShellBuilder> configure)
    {
        Guard.Against.Null(configure);
        _shellConfigurators.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds a shell blueprint with the given name. The supplied delegate runs against a fresh
    /// <see cref="ShellBuilder"/> on every activation / reload. All registered
    /// <see cref="ConfigureAllShells"/> configurators apply first (in registration order), then
    /// the shell-specific <paramref name="configure"/>.
    /// </summary>
    public CShellsBuilder AddShell(string name, Action<ShellBuilder> configure)
    {
        Guard.Against.NullOrWhiteSpace(name);
        Guard.Against.Null(configure);

        Action<ShellBuilder> combined = shellBuilder =>
        {
            foreach (var common in _shellConfigurators)
                common(shellBuilder);
            configure(shellBuilder);
        };

        Services.AddSingleton<IShellBlueprint>(new DelegateShellBlueprint(name, combined));
        return this;
    }

    /// <summary>
    /// Adds a blueprint directly. Used by downstream providers (e.g., FluentStorage) that build
    /// their own <see cref="IShellBlueprint"/> instances.
    /// </summary>
    public CShellsBuilder AddBlueprint(IShellBlueprint blueprint)
    {
        Services.AddSingleton(Guard.Against.Null(blueprint));
        return this;
    }
}
