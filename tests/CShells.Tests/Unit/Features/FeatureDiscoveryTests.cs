using CShells.Features;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Features;

public class FeatureDiscoveryTests
{
    [Fact(DisplayName = "DiscoverFeatures includes descriptors for type-based dependencies outside scanned assemblies")]
    public void DiscoverFeatures_WithTypeDependency_AddsDependencyDescriptor()
    {
        var assembly = TestAssemblyBuilder.CreateTestAssembly(
            ("Dependent", typeof(IShellFeature), [typeof(DependencyFeature)], []));

        var features = FeatureDiscovery
            .DiscoverFeatures([assembly])
            .ToDictionary(static feature => feature.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Dependency", features.Keys);
        Assert.Equal(typeof(DependencyFeature), features["Dependency"].StartupType);
        Assert.Contains("Dependency", features["Dependent"].Dependencies);
    }

    [Fact(DisplayName = "DiscoverFeatures recursively includes type-based dependency descriptors")]
    public void DiscoverFeatures_WithTransitiveTypeDependency_AddsTransitiveDependencyDescriptor()
    {
        var assembly = TestAssemblyBuilder.CreateTestAssembly(
            ("TransitiveDependent", typeof(IShellFeature), [typeof(DependentFeature)], []));

        var features = FeatureDiscovery
            .DiscoverFeatures([assembly])
            .ToDictionary(static feature => feature.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("TransitiveDependent", features.Keys);
        Assert.Contains("Dependent", features.Keys);
        Assert.Contains("Dependency", features.Keys);
        Assert.Contains("Dependent", features["TransitiveDependent"].Dependencies);
        Assert.Contains("Dependency", features["Dependent"].Dependencies);
    }

    [Fact(DisplayName = "DiscoverFeatures handles deep type-based dependency chains")]
    public void DiscoverFeatures_WithDeepTypeDependencyChain_CompletesPredictably()
    {
        var assembly = TestAssemblyBuilder.CreateTestAssembly(
            ("Root", typeof(IShellFeature), [typeof(Chain0Feature)], []));

        var features = FeatureDiscovery
            .DiscoverFeatures([assembly])
            .ToDictionary(static feature => feature.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Root", features.Keys);
        Assert.Contains("Chain0", features.Keys);
        Assert.Contains("Chain63", features.Keys);
        Assert.Contains("Chain0", features["Root"].Dependencies);
    }

    [ShellFeature("Dependency")]
    private sealed class DependencyFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }
    }

    [ShellFeature("Dependent", DependsOn = [typeof(DependencyFeature)])]
    private sealed class DependentFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }
    }

    private abstract class TestFeatureBase : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }
    }

    [ShellFeature("Chain0", DependsOn = [typeof(Chain1Feature)])] private sealed class Chain0Feature : TestFeatureBase;
    [ShellFeature("Chain1", DependsOn = [typeof(Chain2Feature)])] private sealed class Chain1Feature : TestFeatureBase;
    [ShellFeature("Chain2", DependsOn = [typeof(Chain3Feature)])] private sealed class Chain2Feature : TestFeatureBase;
    [ShellFeature("Chain3", DependsOn = [typeof(Chain4Feature)])] private sealed class Chain3Feature : TestFeatureBase;
    [ShellFeature("Chain4", DependsOn = [typeof(Chain5Feature)])] private sealed class Chain4Feature : TestFeatureBase;
    [ShellFeature("Chain5", DependsOn = [typeof(Chain6Feature)])] private sealed class Chain5Feature : TestFeatureBase;
    [ShellFeature("Chain6", DependsOn = [typeof(Chain7Feature)])] private sealed class Chain6Feature : TestFeatureBase;
    [ShellFeature("Chain7", DependsOn = [typeof(Chain8Feature)])] private sealed class Chain7Feature : TestFeatureBase;
    [ShellFeature("Chain8", DependsOn = [typeof(Chain9Feature)])] private sealed class Chain8Feature : TestFeatureBase;
    [ShellFeature("Chain9", DependsOn = [typeof(Chain10Feature)])] private sealed class Chain9Feature : TestFeatureBase;
    [ShellFeature("Chain10", DependsOn = [typeof(Chain11Feature)])] private sealed class Chain10Feature : TestFeatureBase;
    [ShellFeature("Chain11", DependsOn = [typeof(Chain12Feature)])] private sealed class Chain11Feature : TestFeatureBase;
    [ShellFeature("Chain12", DependsOn = [typeof(Chain13Feature)])] private sealed class Chain12Feature : TestFeatureBase;
    [ShellFeature("Chain13", DependsOn = [typeof(Chain14Feature)])] private sealed class Chain13Feature : TestFeatureBase;
    [ShellFeature("Chain14", DependsOn = [typeof(Chain15Feature)])] private sealed class Chain14Feature : TestFeatureBase;
    [ShellFeature("Chain15", DependsOn = [typeof(Chain16Feature)])] private sealed class Chain15Feature : TestFeatureBase;
    [ShellFeature("Chain16", DependsOn = [typeof(Chain17Feature)])] private sealed class Chain16Feature : TestFeatureBase;
    [ShellFeature("Chain17", DependsOn = [typeof(Chain18Feature)])] private sealed class Chain17Feature : TestFeatureBase;
    [ShellFeature("Chain18", DependsOn = [typeof(Chain19Feature)])] private sealed class Chain18Feature : TestFeatureBase;
    [ShellFeature("Chain19", DependsOn = [typeof(Chain20Feature)])] private sealed class Chain19Feature : TestFeatureBase;
    [ShellFeature("Chain20", DependsOn = [typeof(Chain21Feature)])] private sealed class Chain20Feature : TestFeatureBase;
    [ShellFeature("Chain21", DependsOn = [typeof(Chain22Feature)])] private sealed class Chain21Feature : TestFeatureBase;
    [ShellFeature("Chain22", DependsOn = [typeof(Chain23Feature)])] private sealed class Chain22Feature : TestFeatureBase;
    [ShellFeature("Chain23", DependsOn = [typeof(Chain24Feature)])] private sealed class Chain23Feature : TestFeatureBase;
    [ShellFeature("Chain24", DependsOn = [typeof(Chain25Feature)])] private sealed class Chain24Feature : TestFeatureBase;
    [ShellFeature("Chain25", DependsOn = [typeof(Chain26Feature)])] private sealed class Chain25Feature : TestFeatureBase;
    [ShellFeature("Chain26", DependsOn = [typeof(Chain27Feature)])] private sealed class Chain26Feature : TestFeatureBase;
    [ShellFeature("Chain27", DependsOn = [typeof(Chain28Feature)])] private sealed class Chain27Feature : TestFeatureBase;
    [ShellFeature("Chain28", DependsOn = [typeof(Chain29Feature)])] private sealed class Chain28Feature : TestFeatureBase;
    [ShellFeature("Chain29", DependsOn = [typeof(Chain30Feature)])] private sealed class Chain29Feature : TestFeatureBase;
    [ShellFeature("Chain30", DependsOn = [typeof(Chain31Feature)])] private sealed class Chain30Feature : TestFeatureBase;
    [ShellFeature("Chain31", DependsOn = [typeof(Chain32Feature)])] private sealed class Chain31Feature : TestFeatureBase;
    [ShellFeature("Chain32", DependsOn = [typeof(Chain33Feature)])] private sealed class Chain32Feature : TestFeatureBase;
    [ShellFeature("Chain33", DependsOn = [typeof(Chain34Feature)])] private sealed class Chain33Feature : TestFeatureBase;
    [ShellFeature("Chain34", DependsOn = [typeof(Chain35Feature)])] private sealed class Chain34Feature : TestFeatureBase;
    [ShellFeature("Chain35", DependsOn = [typeof(Chain36Feature)])] private sealed class Chain35Feature : TestFeatureBase;
    [ShellFeature("Chain36", DependsOn = [typeof(Chain37Feature)])] private sealed class Chain36Feature : TestFeatureBase;
    [ShellFeature("Chain37", DependsOn = [typeof(Chain38Feature)])] private sealed class Chain37Feature : TestFeatureBase;
    [ShellFeature("Chain38", DependsOn = [typeof(Chain39Feature)])] private sealed class Chain38Feature : TestFeatureBase;
    [ShellFeature("Chain39", DependsOn = [typeof(Chain40Feature)])] private sealed class Chain39Feature : TestFeatureBase;
    [ShellFeature("Chain40", DependsOn = [typeof(Chain41Feature)])] private sealed class Chain40Feature : TestFeatureBase;
    [ShellFeature("Chain41", DependsOn = [typeof(Chain42Feature)])] private sealed class Chain41Feature : TestFeatureBase;
    [ShellFeature("Chain42", DependsOn = [typeof(Chain43Feature)])] private sealed class Chain42Feature : TestFeatureBase;
    [ShellFeature("Chain43", DependsOn = [typeof(Chain44Feature)])] private sealed class Chain43Feature : TestFeatureBase;
    [ShellFeature("Chain44", DependsOn = [typeof(Chain45Feature)])] private sealed class Chain44Feature : TestFeatureBase;
    [ShellFeature("Chain45", DependsOn = [typeof(Chain46Feature)])] private sealed class Chain45Feature : TestFeatureBase;
    [ShellFeature("Chain46", DependsOn = [typeof(Chain47Feature)])] private sealed class Chain46Feature : TestFeatureBase;
    [ShellFeature("Chain47", DependsOn = [typeof(Chain48Feature)])] private sealed class Chain47Feature : TestFeatureBase;
    [ShellFeature("Chain48", DependsOn = [typeof(Chain49Feature)])] private sealed class Chain48Feature : TestFeatureBase;
    [ShellFeature("Chain49", DependsOn = [typeof(Chain50Feature)])] private sealed class Chain49Feature : TestFeatureBase;
    [ShellFeature("Chain50", DependsOn = [typeof(Chain51Feature)])] private sealed class Chain50Feature : TestFeatureBase;
    [ShellFeature("Chain51", DependsOn = [typeof(Chain52Feature)])] private sealed class Chain51Feature : TestFeatureBase;
    [ShellFeature("Chain52", DependsOn = [typeof(Chain53Feature)])] private sealed class Chain52Feature : TestFeatureBase;
    [ShellFeature("Chain53", DependsOn = [typeof(Chain54Feature)])] private sealed class Chain53Feature : TestFeatureBase;
    [ShellFeature("Chain54", DependsOn = [typeof(Chain55Feature)])] private sealed class Chain54Feature : TestFeatureBase;
    [ShellFeature("Chain55", DependsOn = [typeof(Chain56Feature)])] private sealed class Chain55Feature : TestFeatureBase;
    [ShellFeature("Chain56", DependsOn = [typeof(Chain57Feature)])] private sealed class Chain56Feature : TestFeatureBase;
    [ShellFeature("Chain57", DependsOn = [typeof(Chain58Feature)])] private sealed class Chain57Feature : TestFeatureBase;
    [ShellFeature("Chain58", DependsOn = [typeof(Chain59Feature)])] private sealed class Chain58Feature : TestFeatureBase;
    [ShellFeature("Chain59", DependsOn = [typeof(Chain60Feature)])] private sealed class Chain59Feature : TestFeatureBase;
    [ShellFeature("Chain60", DependsOn = [typeof(Chain61Feature)])] private sealed class Chain60Feature : TestFeatureBase;
    [ShellFeature("Chain61", DependsOn = [typeof(Chain62Feature)])] private sealed class Chain61Feature : TestFeatureBase;
    [ShellFeature("Chain62", DependsOn = [typeof(Chain63Feature)])] private sealed class Chain62Feature : TestFeatureBase;
    [ShellFeature("Chain63")] private sealed class Chain63Feature : TestFeatureBase;
}
