using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Features;

public class FeatureAssemblyProviderTests
{
    [Fact]
    public async Task ResolveAssembliesAsync_AggregatesExplicitProviderContributionsInOrder()
    {
        var firstAssembly = typeof(FeatureAssemblyProviderTests).Assembly;
        var secondAssembly = typeof(CShells.DependencyInjection.CShellsBuilder).Assembly;
        IFeatureAssemblyProvider[] providers =
        [
            new ExplicitFeatureAssemblyProvider([firstAssembly]),
            new ExplicitFeatureAssemblyProvider([secondAssembly])
        ];
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var assemblies = await FeatureAssemblyResolver.ResolveAssembliesAsync(providers, serviceProvider);

        Assert.Equal([firstAssembly, secondAssembly], assemblies);
    }

    [Fact]
    public async Task ResolveAssembliesAsync_DeduplicatesDuplicateAssemblyContributionsUsingFirstSeenOrder()
    {
        var firstAssembly = typeof(FeatureAssemblyProviderTests).Assembly;
        var secondAssembly = typeof(CShells.DependencyInjection.CShellsBuilder).Assembly;
        IFeatureAssemblyProvider[] providers =
        [
            new ExplicitFeatureAssemblyProvider([firstAssembly, secondAssembly]),
            new ExplicitFeatureAssemblyProvider([secondAssembly, firstAssembly])
        ];
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var assemblies = await FeatureAssemblyResolver.ResolveAssembliesAsync(providers, serviceProvider);

        Assert.Equal([firstAssembly, secondAssembly], assemblies);
    }
}
