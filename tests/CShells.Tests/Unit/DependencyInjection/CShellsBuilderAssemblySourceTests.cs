using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.DependencyInjection;

public class CShellsBuilderAssemblySourceTests
{
    [Fact]
    public void FromAssemblies_AppendsExplicitProvidersInCallOrder()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        var firstAssembly = typeof(CShellsBuilderAssemblySourceTests).Assembly;
        var secondAssembly = typeof(CShellsBuilder).Assembly;
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithAssemblies(firstAssembly);
        builder.WithAssemblies(secondAssembly);

        var providers = builder.BuildFeatureAssemblyProviders(serviceProvider);

        Assert.Collection(
            providers,
            provider => Assert.Equal(typeof(ExplicitFeatureAssemblyProvider), provider.GetType()),
            provider => Assert.Equal(typeof(ExplicitFeatureAssemblyProvider), provider.GetType()));
    }

    [Fact]
    public async Task FromAssemblies_WithEmptyInput_ActivatesExplicitModeWithoutAssemblies()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithAssemblies();

        Assert.True(builder.UsesExplicitFeatureAssemblyProviders);
        Assert.Empty(await FeatureAssemblyResolver.ResolveAssembliesAsync(builder.BuildFeatureAssemblyProviders(serviceProvider), serviceProvider));
    }

    [Fact]
    public void FromAssemblies_WithNullAssembly_ThrowsArgumentException()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        Assembly[] assemblies = [typeof(CShellsBuilderAssemblySourceTests).Assembly, null!];

        var exception = Assert.Throws<ArgumentException>(() => builder.WithAssemblies(assemblies));

        Assert.Equal("assemblies", exception.ParamName);
    }

    [Fact]
    public async Task FromAssemblyContaining_UsesMarkerTypeAssemblyAndActivatesExplicitMode()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithAssemblyContaining<MarkerService>();

        Assert.True(builder.UsesExplicitFeatureAssemblyProviders);
        Assert.IsType<ExplicitFeatureAssemblyProvider>(Assert.Single(builder.BuildFeatureAssemblyProviders(serviceProvider)));

        var assemblies = await FeatureAssemblyResolver.ResolveAssembliesAsync(builder.BuildFeatureAssemblyProviders(serviceProvider), serviceProvider);
        Assert.Equal(typeof(MarkerService).Assembly, Assert.Single(assemblies));
    }

    [Fact]
    public async Task FromAssemblyContaining_ComposesAdditivelyInRegistrationOrder()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        var secondAssembly = typeof(CShellsBuilder).Assembly;
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithAssemblyContaining<MarkerService>();
        builder.WithAssemblies(secondAssembly);

        var assemblies = await FeatureAssemblyResolver.ResolveAssembliesAsync(builder.BuildFeatureAssemblyProviders(serviceProvider), serviceProvider);

        Assert.Equal([typeof(MarkerService).Assembly, secondAssembly], assemblies);
    }

    [Fact]
    public void FromHostAssemblies_AppendsHostProviderAndActivatesExplicitMode()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithHostAssemblies();

        Assert.True(builder.UsesExplicitFeatureAssemblyProviders);
        Assert.IsType<HostFeatureAssemblyProvider>(Assert.Single(builder.BuildFeatureAssemblyProviders(serviceProvider)));
    }

    [Fact]
    public void WithAssemblyProvider_GenericOverloadResolvesProviderFromRootServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestFeatureAssemblyProvider>();
        var builder = new CShellsBuilder(services);
        using var serviceProvider = services.BuildServiceProvider();

        builder.WithAssemblyProvider<TestFeatureAssemblyProvider>();

        var providers = builder.BuildFeatureAssemblyProviders(serviceProvider);

        Assert.Same(serviceProvider.GetRequiredService<TestFeatureAssemblyProvider>(), Assert.Single(providers));
    }

    [Fact]
    public void WithAssemblyProvider_GenericOverloadThrowsWhenProviderIsMissingFromRootServiceProvider()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithAssemblyProvider<MissingFeatureAssemblyProvider>();

        var exception = Assert.Throws<InvalidOperationException>(() => builder.BuildFeatureAssemblyProviders(serviceProvider));

        Assert.Contains(typeof(MissingFeatureAssemblyProvider).FullName!, exception.Message);
    }

    [Fact]
    public void WithAssemblyProvider_InstanceOverloadAppendsProvider()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        var provider = new DelegateFeatureAssemblyProvider(_ => [typeof(CShellsBuilderAssemblySourceTests).Assembly]);
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithAssemblyProvider(provider);

        Assert.Same(provider, Assert.Single(builder.BuildFeatureAssemblyProviders(serviceProvider)));
    }

    [Fact]
    public async Task WithAssemblyProvider_FactoryOverloadAppendsProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MarkerService>();
        var builder = new CShellsBuilder(services);
        using var serviceProvider = services.BuildServiceProvider();

        builder.WithAssemblyProvider(_ => new DelegateFeatureAssemblyProvider(_ => [typeof(MarkerService).Assembly]));

        var provider = Assert.IsType<DelegateFeatureAssemblyProvider>(Assert.Single(builder.BuildFeatureAssemblyProviders(serviceProvider)));
        var assemblies = await provider.GetAssembliesAsync(serviceProvider);
        Assert.Equal(typeof(MarkerService).Assembly, Assert.Single(assemblies));
    }

    [Fact]
    public async Task WithAssemblyProvider_OverloadsComposeAdditivelyInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestFeatureAssemblyProvider>();
        var builder = new CShellsBuilder(services);
        var instanceProvider = new DelegateFeatureAssemblyProvider(_ => [typeof(CShellsBuilderAssemblySourceTests).Assembly]);
        using var serviceProvider = services.BuildServiceProvider();

        builder.WithAssemblyProvider<TestFeatureAssemblyProvider>();
        builder.WithAssemblyProvider(instanceProvider);
        builder.WithAssemblyProvider(_ => new DelegateFeatureAssemblyProvider(_ => [typeof(MarkerService).Assembly]));

        var providers = builder.BuildFeatureAssemblyProviders(serviceProvider);

        Assert.Equal(3, providers.Count);
        Assert.Same(serviceProvider.GetRequiredService<TestFeatureAssemblyProvider>(), providers[0]);
        Assert.Same(instanceProvider, providers[1]);

        var delegateProvider = Assert.IsType<DelegateFeatureAssemblyProvider>(providers[2]);
        var assemblies = await delegateProvider.GetAssembliesAsync(serviceProvider);
        Assert.Equal(typeof(MarkerService).Assembly, Assert.Single(assemblies));
    }

    [Fact]
    public void WithAssemblyProvider_InstanceOverloadGuardsNullProvider()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.WithAssemblyProvider(provider: null!));

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void WithAssemblyProvider_FactoryOverloadGuardsNullFactory()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.WithAssemblyProvider(factory: null!));

        Assert.Equal("factory", exception.ParamName);
    }

    [Fact]
    public void WithAssemblyProvider_FactoryOverloadRejectsNullProviderResults()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        builder.WithAssemblyProvider(_ => null!);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.BuildFeatureAssemblyProviders(serviceProvider));

        Assert.Contains("returned null", exception.Message);
    }

    private sealed class MarkerService;

    private sealed class MissingFeatureAssemblyProvider : IFeatureAssemblyProvider
    {
        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<Assembly>>([]);
    }

    private sealed class TestFeatureAssemblyProvider : IFeatureAssemblyProvider
    {
        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<Assembly>>([typeof(CShellsBuilderAssemblySourceTests).Assembly]);
    }

    private sealed class DelegateFeatureAssemblyProvider(Func<IServiceProvider, IEnumerable<Assembly>> getAssemblies) : IFeatureAssemblyProvider
    {
        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
            Task.FromResult(getAssemblies(serviceProvider));
    }
}
