using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Configuration;

public class ConfiguringShellSettingsProviderTests
{
    [Fact]
    public async Task ConfigureAllShells_AppliesFeaturesToCodeFirstShells()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddCShells(shells =>
        {
            shells.AddShell("Shell1", shell => shell.WithFeature("Feature1"));
            shells.AddShell("Shell2", shell => shell.WithFeature("Feature2"));
            shells.ConfigureAllShells(shell => shell.WithFeature("SharedFeature"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act
        var settings = (await provider.GetShellSettingsAsync()).ToList();

        // Assert
        Assert.Equal(2, settings.Count);

        var shell1 = settings.Single(s => s.Id.Name == "Shell1");
        Assert.Contains("Feature1", shell1.EnabledFeatures);
        Assert.Contains("SharedFeature", shell1.EnabledFeatures);

        var shell2 = settings.Single(s => s.Id.Name == "Shell2");
        Assert.Contains("Feature2", shell2.EnabledFeatures);
        Assert.Contains("SharedFeature", shell2.EnabledFeatures);
    }

    [Fact]
    public async Task ConfigureAllShells_AppliesConfigurationToAllShells()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddCShells(shells =>
        {
            shells.AddShell("Shell1", shell => shell.WithFeature("Feature1"));
            shells.AddShell("Shell2", shell => shell.WithFeature("Feature2"));
            shells.ConfigureAllShells(shell => shell.WithConfiguration("Plan", "Enterprise"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act
        var settings = (await provider.GetShellSettingsAsync()).ToList();

        // Assert
        foreach (var shell in settings)
        {
            Assert.Equal("Enterprise", shell.ConfigurationData["Plan"]);
        }
    }

    [Fact]
    public async Task ConfigureAllShells_AppliesFeaturesToProviderShells()
    {
        // Arrange
        var services = new ServiceCollection();
        var providerShells = new List<ShellSettings>
        {
            new(new("ProviderShell"), ["ProviderFeature"])
        };

        services.AddCShells(shells =>
        {
            shells.WithProvider(new InMemoryShellSettingsProvider(providerShells));
            shells.ConfigureAllShells(shell => shell.WithFeature("SharedFeature"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act
        var settings = (await provider.GetShellSettingsAsync()).ToList();

        // Assert
        var shell = settings.Single();
        Assert.Contains("ProviderFeature", shell.EnabledFeatures);
        Assert.Contains("SharedFeature", shell.EnabledFeatures);
    }

    [Fact]
    public async Task ConfigureAllShells_DoesNotCompound_OnMultipleEnumerations()
    {
        // Arrange
        var services = new ServiceCollection();
        var providerShells = new List<ShellSettings>
        {
            new(new("Shell1"), ["Feature1"])
        };

        services.AddCShells(shells =>
        {
            shells.WithProvider(new InMemoryShellSettingsProvider(providerShells));
            shells.ConfigureAllShells(shell => shell.WithFeature("SharedFeature"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act - enumerate multiple times
        var firstCall = (await provider.GetShellSettingsAsync()).ToList();
        var secondCall = (await provider.GetShellSettingsAsync()).ToList();
        var thirdCall = (await provider.GetShellSettingsAsync()).ToList();

        // Assert - features should be the same on every call, not compounded
        Assert.Equal(firstCall.Single().EnabledFeatures, secondCall.Single().EnabledFeatures);
        Assert.Equal(firstCall.Single().EnabledFeatures, thirdCall.Single().EnabledFeatures);
        Assert.Equal(2, thirdCall.Single().EnabledFeatures.Count); // Feature1 + SharedFeature
    }

    [Fact]
    public async Task ConfigureAllShells_DoesNotMutateOriginalProviderInstances()
    {
        // Arrange
        var originalSettings = new ShellSettings(new("Shell1"), ["Feature1"]);
        var providerShells = new List<ShellSettings> { originalSettings };

        var services = new ServiceCollection();

        services.AddCShells(shells =>
        {
            shells.WithProvider(new InMemoryShellSettingsProvider(providerShells));
            shells.ConfigureAllShells(shell => shell
                .WithFeature("SharedFeature")
                .WithConfiguration("Key", "Value"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act
        _ = (await provider.GetShellSettingsAsync()).ToList();

        // Assert - original instance should not be mutated
        Assert.Equal(["Feature1"], originalSettings.EnabledFeatures);
        Assert.Empty(originalSettings.ConfigurationData);
    }

    [Fact]
    public async Task ConfigureAllShells_FeatureConfiguratorsDoNotCompound()
    {
        // Arrange
        var callCount = 0;
        var services = new ServiceCollection();
        var providerShells = new List<ShellSettings>
        {
            new(new("Shell1"), ["Feature1"])
        };

        services.AddCShells(shells =>
        {
            shells.WithProvider(new InMemoryShellSettingsProvider(providerShells));
            shells.ConfigureAllShells(shell => shell.WithFeature<TestFeature>(f => callCount++));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act - call GetShellSettingsAsync multiple times
        var firstResult = (await provider.GetShellSettingsAsync()).ToList();
        var secondResult = (await provider.GetShellSettingsAsync()).ToList();

        // Assert - each call's settings should have exactly one configurator, not compounded
        var firstConfigurator = firstResult.Single().FeatureConfigurators;
        var secondConfigurator = secondResult.Single().FeatureConfigurators;

        Assert.Single(firstConfigurator);
        Assert.Single(secondConfigurator);

        // Invoke both configurators to verify they each call once
        callCount = 0;
        firstConfigurator[nameof(TestFeature)].Invoke(new TestFeature());
        Assert.Equal(1, callCount);

        callCount = 0;
        secondConfigurator[nameof(TestFeature)].Invoke(new TestFeature());
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ConfigureAllShells_MultipleConfiguratorsApplyInOrder()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddCShells(shells =>
        {
            shells.AddShell("Shell1", shell => shell.WithFeature("Feature1"));
            shells.ConfigureAllShells(shell => shell.WithConfiguration("Key", "First"));
            shells.ConfigureAllShells(shell => shell.WithConfiguration("Key", "Second"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act
        var settings = (await provider.GetShellSettingsAsync()).ToList();

        // Assert - last configurator wins
        Assert.Equal("Second", settings.Single().ConfigurationData["Key"]);
    }

    [Fact]
    public async Task ConfigureAllShells_AppliesViaGetShellSettingsByIdToo()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddCShells(shells =>
        {
            shells.AddShell("Shell1", shell => shell.WithFeature("Feature1"));
            shells.ConfigureAllShells(shell => shell.WithFeature("SharedFeature"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act
        var settings = await provider.GetShellSettingsAsync(new ShellId("Shell1"));

        // Assert
        Assert.NotNull(settings);
        Assert.Contains("Feature1", settings.EnabledFeatures);
        Assert.Contains("SharedFeature", settings.EnabledFeatures);
    }

    [Fact]
    public async Task ConfigureAllShells_GetShellSettingsById_ReturnsNullForMissingShell()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddCShells(shells =>
        {
            shells.AddShell("Shell1", shell => shell.WithFeature("Feature1"));
            shells.ConfigureAllShells(shell => shell.WithFeature("SharedFeature"));
        });

        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellSettingsProvider>();

        // Act
        var settings = await provider.GetShellSettingsAsync(new ShellId("NonExistent"));

        // Assert
        Assert.Null(settings);
    }

    [ShellFeature(nameof(TestFeature))]
    private class TestFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) { }
    }
}
