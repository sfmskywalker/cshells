using CShells.Features;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        var logs = new List<(LogLevel Level, string Message)>();
        await using var host = BuildHost(
            cshells => cshells
                .WithAssemblyContaining<UnknownFeatureDependencyTests>()
                .AddShell("payments", shell => shell.WithFeature("MissingFeature")),
            services => services.AddSingleton<ILogger<ShellProviderBuilder>>(new CapturingLogger<ShellProviderBuilder>(logs)));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("payments");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Empty(settings.EnabledFeatures);
        Assert.Contains(logs, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("MissingFeature", StringComparison.Ordinal) &&
            entry.Message.Contains("available features only", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildHost(Action<CShellsBuilder> configure, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        configureServices?.Invoke(services);
        services.AddCShells(configure);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLogger<T>(List<(LogLevel Level, string Message)> sink) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

[ShellFeature("Known")]
public sealed class KnownFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
