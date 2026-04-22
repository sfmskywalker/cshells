using CShells.Lifecycle.Blueprints;
using Microsoft.Extensions.Configuration;

namespace CShells.Tests.Unit.Lifecycle.Blueprints;

public class ConfigurationShellBlueprintTests
{
    private sealed class MutableMemoryProvider : ConfigurationProvider
    {
        public new void Set(string key, string? value) => Data[key] = value;
    }
    [Fact(DisplayName = "ComposeAsync reflects runtime changes to the underlying IConfiguration")]
    public async Task ComposeAsync_ReflectsRuntimeConfigChanges()
    {
        // A mutable backing provider: swap its dictionary between composes and observe the change.
        var provider = new MutableMemoryProvider();
        provider.Set("Features:0:Name", "Core");

        var root = new ConfigurationRoot([provider]);
        var bp = new ConfigurationShellBlueprint("payments", root);

        var first = await bp.ComposeAsync();
        Assert.Equal(["Core"], first.EnabledFeatures);

        provider.Set("Features:0:Name", "Analytics");
        root.Reload();

        var second = await bp.ComposeAsync();
        Assert.Equal(["Analytics"], second.EnabledFeatures);
    }

    [Fact(DisplayName = "Feature settings are promoted to ConfigurationData with feature-name prefix")]
    public async Task FeatureSettings_AppearInConfigurationData()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:0:Name"] = "Analytics",
            ["Features:0:Settings:TopPostsCount"] = "10",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var settings = await bp.ComposeAsync();

        Assert.True(settings.ConfigurationData.ContainsKey("Analytics:TopPostsCount"));
        Assert.Equal("10", settings.ConfigurationData["Analytics:TopPostsCount"]);
    }
}
