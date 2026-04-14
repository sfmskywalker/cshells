using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Features;

public class HostFeatureAssemblyProviderTests
{
    [Fact]
    public async Task GetAssembliesAsync_MatchesSharedHostResolutionAlgorithm()
    {
        var provider = new HostFeatureAssemblyProvider();
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var expected = FeatureAssemblyResolver.ResolveHostAssemblies().Select(assembly => assembly.FullName).ToArray();
        var actual = (await provider.GetAssembliesAsync(serviceProvider)).Select(assembly => assembly.FullName).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ResolveAssembliesAsync_DeduplicatesRepeatedHostProviderContributions()
    {
        IFeatureAssemblyProvider[] providers = [new HostFeatureAssemblyProvider(), new HostFeatureAssemblyProvider()];
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var expected = (await new HostFeatureAssemblyProvider().GetAssembliesAsync(serviceProvider)).Select(assembly => assembly.FullName).Distinct().ToArray();
        var actual = (await FeatureAssemblyResolver.ResolveAssembliesAsync(providers, serviceProvider)).Select(assembly => assembly.FullName).ToArray();

        Assert.Equal(expected, actual);
    }
}
