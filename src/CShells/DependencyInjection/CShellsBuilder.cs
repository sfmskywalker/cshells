using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.DependencyInjection;

/// <summary>
/// Builder for configuring CShells services with a fluent API.
/// Supports both provider-based and code-first shell configuration.
/// </summary>
public class CShellsBuilder
{
    private readonly List<ShellSettings> _codeFirstShells = new();
    private readonly List<Action<IServiceProvider, List<IShellSettingsProvider>>> _providerRegistrations = new();
    private readonly List<Func<IServiceProvider, IFeatureAssemblyProvider>> _featureAssemblyProviderRegistrations = [];

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
    /// Gets all code-first shell settings configured via AddShell.
    /// </summary>
    internal IReadOnlyList<ShellSettings> CodeFirstShells => _codeFirstShells.AsReadOnly();

    /// <summary>
    /// Gets a value indicating whether explicit feature assembly providers were configured.
    /// </summary>
    internal bool UsesExplicitFeatureAssemblyProviders => _featureAssemblyProviderRegistrations.Count > 0;

    /// <summary>
    /// Registers a provider registration action.
    /// </summary>
    internal void RegisterProvider(Action<IServiceProvider, List<IShellSettingsProvider>> registration)
    {
        _providerRegistrations.Add(registration);
    }

    /// <summary>
    /// Builds all registered providers and returns them.
    /// </summary>
    internal List<IShellSettingsProvider> BuildProviders(IServiceProvider serviceProvider)
    {
        var providers = new List<IShellSettingsProvider>();
        
        foreach (var registration in _providerRegistrations)
        {
            registration(serviceProvider, providers);
        }
        
        return providers;
    }

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
    internal IReadOnlyCollection<Assembly> BuildFeatureAssemblies(IServiceProvider serviceProvider)
    {
        Guard.Against.Null(serviceProvider);

        return UsesExplicitFeatureAssemblyProviders
            ? CShells.Features.FeatureAssemblyResolver.ResolveAssemblies(BuildFeatureAssemblyProviders(serviceProvider), serviceProvider)
            : CShells.Features.FeatureAssemblyResolver.ResolveHostAssemblies();
    }

    /// <summary>
    /// Adds a shell using a fluent builder.
    /// </summary>
    /// <param name="configure">Configuration action for the shell builder.</param>
    /// <returns>This builder for method chaining.</returns>
    public CShellsBuilder AddShell(Action<ShellBuilder> configure)
    {
        Guard.Against.Null(configure);
        var shellBuilder = new ShellBuilder(new ShellId(Guid.NewGuid().ToString()));
        configure(shellBuilder);
        _codeFirstShells.Add(shellBuilder.Build());
        return this;
    }

    /// <summary>
    /// Adds a shell with the specified ID using a fluent builder.
    /// </summary>
    /// <param name="id">The shell identifier.</param>
    /// <param name="configure">Configuration action for the shell builder.</param>
    /// <returns>This builder for method chaining.</returns>
    public CShellsBuilder AddShell(string id, Action<ShellBuilder> configure)
    {
        Guard.Against.Null(id);
        Guard.Against.Null(configure);
        var shellBuilder = new ShellBuilder(new ShellId(id));
        configure(shellBuilder);
        _codeFirstShells.Add(shellBuilder.Build());
        return this;
    }

    /// <summary>
    /// Adds a pre-configured shell.
    /// </summary>
    /// <param name="settings">The shell settings.</param>
    /// <returns>This builder for method chaining.</returns>
    public CShellsBuilder AddShell(ShellSettings settings)
    {
        _codeFirstShells.Add(Guard.Against.Null(settings));
        return this;
    }
}
