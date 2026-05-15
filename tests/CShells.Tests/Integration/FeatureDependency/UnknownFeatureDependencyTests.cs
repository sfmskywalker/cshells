using CShells.Features;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.FeatureDependency;

/// <summary>
/// Tests for unknown feature dependency handling in <see cref="FeatureDependencyResolver"/>.
/// </summary>
public class UnknownFeatureDependencyTests
{
    private readonly FeatureDependencyResolver _resolver = new();

    [Theory(DisplayName = "GetOrderedFeatures with unknown dependency throws with feature name")]
    [MemberData(nameof(FeatureDependencyData.UnknownDependencyCases), MemberType = typeof(FeatureDependencyData))]
    public void GetOrderedFeatures_WithUnknownDependency_ThrowsWithFeatureName(IEnumerable<string> roots, string missingFeature, string[] dependencyMap)
    {
        // Arrange
        var featureList = FeatureTestHelpers.ParseFeatureDependencies(dependencyMap);
        var features = FeatureTestHelpers.CreateFeatureDictionary(featureList);

        // Act & Assert
        var ex = Assert.Throws<FeatureNotFoundException>(() => _resolver.GetOrderedFeatures(roots, features));
        Assert.Contains(missingFeature, ex.Message);
        Assert.Contains("not found", ex.Message);
    }

    [Theory(DisplayName = "ResolveDependencies with unknown feature throws FeatureNotFoundException")]
    [MemberData(nameof(FeatureDependencyData.UnknownDependencyCases), MemberType = typeof(FeatureDependencyData))]
    public void ResolveDependencies_WithUnknownFeature_ThrowsFeatureNotFoundException(IEnumerable<string> _, string missingFeature, string[] dependencyMap)
    {
        // Arrange
        var featureList = FeatureTestHelpers.ParseFeatureDependencies(dependencyMap);
        var features = FeatureTestHelpers.CreateFeatureDictionary(featureList);

        // Act & Assert
        var ex = Assert.Throws<FeatureNotFoundException>(() => _resolver.ResolveDependencies(missingFeature, features));
        Assert.Contains(missingFeature, ex.Message);
        Assert.Contains("not found", ex.Message);
    }

    [Fact(DisplayName = "Activation ignores unknown disabled feature declarations")]
    public async Task ActivateAsync_UnknownDisabledFeature_DoesNotPreventActivation()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblyContaining<UnknownFeatureDependencyTests>()
            .AddShell("payments", shell => shell
                .WithFeature<KnownFeature>()
                .WithFeature(FeatureEntry.Disabled("MissingFeature"))));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("payments");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Equal(["Known"], settings.EnabledFeatures);
        Assert.Equal(["MissingFeature"], settings.DisabledFeatures);
    }

    [Fact(DisplayName = "Activation ignores unknown positive feature declarations")]
    public async Task ActivateAsync_UnknownEnabledFeature_DoesNotPreventActivation()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblyContaining<UnknownFeatureDependencyTests>()
            .AddShell("payments", shell => shell.WithFeature("MissingFeature")));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("payments");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Empty(settings.EnabledFeatures);
    }

    private static ServiceProvider BuildHost(Action<CShellsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCShells(configure);
        return services.BuildServiceProvider();
    }

}

[ShellFeature("Known")]
public sealed class KnownFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
