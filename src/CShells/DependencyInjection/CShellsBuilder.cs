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
    private readonly List<IShellBlueprint> _inlineBlueprints = [];
    private readonly List<Func<IServiceProvider, IShellBlueprintProvider>> _providerFactories = [];
    private readonly List<string> _preWarmNames = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="CShellsBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CShellsBuilder(IServiceCollection services)
    {
        Services = Guard.Against.Null(services);
    }

    /// <summary>Blueprints contributed via <see cref="AddShell"/> and <see cref="AddBlueprint"/>, in registration order.</summary>
    internal IReadOnlyList<IShellBlueprint> InlineBlueprints => _inlineBlueprints;

    /// <summary>Factories that resolve additional <see cref="IShellBlueprintProvider"/> instances at DI-resolution time.</summary>
    internal IReadOnlyList<Func<IServiceProvider, IShellBlueprintProvider>> ProviderFactories => _providerFactories;

    /// <summary>Shell names to activate at host startup. Populated by <see cref="PreWarmShells"/>.</summary>
    internal IReadOnlyList<string> PreWarmNames => _preWarmNames;

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
    /// <remarks>
    /// Blueprints added here are vended by the built-in <c>InMemoryShellBlueprintProvider</c>
    /// at DI-resolution time. Activation is lazy — the first request for the shell triggers
    /// construction.
    /// </remarks>
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

        _inlineBlueprints.Add(new DelegateShellBlueprint(name, combined));
        return this;
    }

    /// <summary>
    /// Adds a blueprint directly to the in-memory provider.
    /// </summary>
    public CShellsBuilder AddBlueprint(IShellBlueprint blueprint)
    {
        _inlineBlueprints.Add(Guard.Against.Null(blueprint));
        return this;
    }

    /// <summary>
    /// Registers an additional <see cref="IShellBlueprintProvider"/> resolved from DI at
    /// composite-construction time. Providers are probed in registration order for lookup
    /// precedence.
    /// </summary>
    public CShellsBuilder AddBlueprintProvider(Func<IServiceProvider, IShellBlueprintProvider> factory)
    {
        _providerFactories.Add(Guard.Against.Null(factory));
        return this;
    }

    /// <summary>
    /// Records a list of shell names to activate during host startup. Pre-warming is optional;
    /// without it, shells activate lazily on first request.
    /// </summary>
    /// <remarks>
    /// A pre-warm activation failure is logged and does not abort host startup. Callers who
    /// need strict pre-warming should activate from their own hosted service with an explicit
    /// error policy.
    /// </remarks>
    public CShellsBuilder PreWarmShells(params string[] names)
    {
        Guard.Against.Null(names);
        foreach (var name in names)
            _preWarmNames.Add(Guard.Against.NullOrWhiteSpace(name));
        return this;
    }
}
