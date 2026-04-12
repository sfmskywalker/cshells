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

        CShellsBuilderExtensions.FromAssemblies(builder, firstAssembly);
        CShellsBuilderExtensions.FromAssemblies(builder, secondAssembly);

        var providers = builder.BuildFeatureAssemblyProviders(serviceProvider);

        Assert.Collection(
            providers,
            provider => Assert.Equal(typeof(ExplicitFeatureAssemblyProvider), provider.GetType()),
            provider => Assert.Equal(typeof(ExplicitFeatureAssemblyProvider), provider.GetType()));
    }

    [Fact]
    public void FromAssemblies_WithEmptyInput_ActivatesExplicitModeWithoutAssemblies()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        CShellsBuilderExtensions.FromAssemblies(builder);

        Assert.True(builder.UsesExplicitFeatureAssemblyProviders);
        Assert.Empty(FeatureAssemblyResolver.ResolveAssemblies(builder.BuildFeatureAssemblyProviders(serviceProvider), serviceProvider));
    }

    [Fact]
    public void FromAssemblies_WithNullAssembly_ThrowsArgumentException()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        Assembly[] assemblies = [typeof(CShellsBuilderAssemblySourceTests).Assembly, null!];

        var exception = Assert.Throws<ArgumentException>(() => CShellsBuilderExtensions.FromAssemblies(builder, assemblies));

        Assert.Equal("assemblies", exception.ParamName);
    }

    [Fact]
    public void FromHostAssemblies_AppendsHostProviderAndActivatesExplicitMode()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        CShellsBuilderExtensions.FromHostAssemblies(builder);

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

        CShellsBuilderExtensions.WithAssemblyProvider<TestFeatureAssemblyProvider>(builder);

        var providers = builder.BuildFeatureAssemblyProviders(serviceProvider);

        Assert.Same(serviceProvider.GetRequiredService<TestFeatureAssemblyProvider>(), Assert.Single(providers));
    }

    [Fact]
    public void WithAssemblyProvider_GenericOverloadThrowsWhenProviderIsMissingFromRootServiceProvider()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        CShellsBuilderExtensions.WithAssemblyProvider<MissingFeatureAssemblyProvider>(builder);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.BuildFeatureAssemblyProviders(serviceProvider));

        Assert.Contains(typeof(MissingFeatureAssemblyProvider).FullName!, exception.Message);
    }

    [Fact]
    public void WithAssemblyProvider_InstanceOverloadAppendsProvider()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        var provider = new DelegateFeatureAssemblyProvider(_ => [typeof(CShellsBuilderAssemblySourceTests).Assembly]);
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        CShellsBuilderExtensions.WithAssemblyProvider(builder, provider);

        Assert.Same(provider, Assert.Single(builder.BuildFeatureAssemblyProviders(serviceProvider)));
    }

    [Fact]
    public void WithAssemblyProvider_FactoryOverloadAppendsProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MarkerService>();
        var builder = new CShellsBuilder(services);
        using var serviceProvider = services.BuildServiceProvider();

        CShellsBuilderExtensions.WithAssemblyProvider(builder, _ => new DelegateFeatureAssemblyProvider(_ => [typeof(MarkerService).Assembly]));

        var provider = Assert.IsType<DelegateFeatureAssemblyProvider>(Assert.Single(builder.BuildFeatureAssemblyProviders(serviceProvider)));
        Assert.Equal(typeof(MarkerService).Assembly, Assert.Single(provider.GetAssemblies(serviceProvider)));
    }

    [Fact]
    public void WithAssemblyProvider_OverloadsComposeAdditivelyInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestFeatureAssemblyProvider>();
        var builder = new CShellsBuilder(services);
        var instanceProvider = new DelegateFeatureAssemblyProvider(_ => [typeof(CShellsBuilderAssemblySourceTests).Assembly]);
        using var serviceProvider = services.BuildServiceProvider();

        CShellsBuilderExtensions.WithAssemblyProvider<TestFeatureAssemblyProvider>(builder);
        CShellsBuilderExtensions.WithAssemblyProvider(builder, instanceProvider);
        CShellsBuilderExtensions.WithAssemblyProvider(builder, _ => new DelegateFeatureAssemblyProvider(_ => [typeof(MarkerService).Assembly]));

        Assert.Collection(
            builder.BuildFeatureAssemblyProviders(serviceProvider),
            provider => Assert.Same(serviceProvider.GetRequiredService<TestFeatureAssemblyProvider>(), provider),
            provider => Assert.Same(instanceProvider, provider),
            provider =>
            {
                var delegateProvider = Assert.IsType<DelegateFeatureAssemblyProvider>(provider);
                Assert.Equal(typeof(MarkerService).Assembly, Assert.Single(delegateProvider.GetAssemblies(serviceProvider)));
            });
    }

    [Fact]
    public void WithAssemblyProvider_InstanceOverloadGuardsNullProvider()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => CShellsBuilderExtensions.WithAssemblyProvider(builder, provider: null!));

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void WithAssemblyProvider_FactoryOverloadGuardsNullFactory()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => CShellsBuilderExtensions.WithAssemblyProvider(builder, factory: null!));

        Assert.Equal("factory", exception.ParamName);
    }

    [Fact]
    public void WithAssemblyProvider_FactoryOverloadRejectsNullProviderResults()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        CShellsBuilderExtensions.WithAssemblyProvider(builder, _ => null!);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.BuildFeatureAssemblyProviders(serviceProvider));

        Assert.Contains("returned null", exception.Message);
    }

    private sealed class MarkerService;

    private sealed class MissingFeatureAssemblyProvider : IFeatureAssemblyProvider
    {
        public IEnumerable<Assembly> GetAssemblies(IServiceProvider serviceProvider) => [];
    }

    private sealed class TestFeatureAssemblyProvider : IFeatureAssemblyProvider
    {
        public IEnumerable<Assembly> GetAssemblies(IServiceProvider serviceProvider) => [typeof(CShellsBuilderAssemblySourceTests).Assembly];
    }

    private sealed class DelegateFeatureAssemblyProvider(Func<IServiceProvider, IEnumerable<Assembly>> getAssemblies) : IFeatureAssemblyProvider
    {
        public IEnumerable<Assembly> GetAssemblies(IServiceProvider serviceProvider) => getAssemblies(serviceProvider);
    }
}
