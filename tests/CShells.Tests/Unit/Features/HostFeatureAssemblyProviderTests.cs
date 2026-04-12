using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Features;

public class HostFeatureAssemblyProviderTests
{
    [Fact]
    public void GetAssemblies_MatchesSharedHostResolutionAlgorithm()
    {
        var provider = new HostFeatureAssemblyProvider();
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var expected = FeatureAssemblyResolver.ResolveHostAssemblies().Select(assembly => assembly.FullName).ToArray();
        var actual = provider.GetAssemblies(serviceProvider).Select(assembly => assembly.FullName).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveAssemblies_DeduplicatesRepeatedHostProviderContributions()
    {
        IFeatureAssemblyProvider[] providers = [new HostFeatureAssemblyProvider(), new HostFeatureAssemblyProvider()];
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var expected = new HostFeatureAssemblyProvider().GetAssemblies(serviceProvider).Select(assembly => assembly.FullName).Distinct().ToArray();
        var actual = FeatureAssemblyResolver.ResolveAssemblies(providers, serviceProvider).Select(assembly => assembly.FullName).ToArray();

        Assert.Equal(expected, actual);
    }
}
