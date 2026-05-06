using CShells.Lifecycle.Blueprints;
using CShells.Configuration;
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

    [Fact(DisplayName = "Object-map inner Name is configuration, not feature identity")]
    public async Task ComposeAsync_ObjectMapInnerName_IsConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:Identity:Name"] = "",
            ["Features:Identity:SigningKey"] = "secret",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var settings = await bp.ComposeAsync();

        Assert.Equal(["Identity"], settings.EnabledFeatures);
        Assert.Equal("", settings.ConfigurationData["Identity:Name"]);
        Assert.Equal("secret", settings.ConfigurationData["Identity:SigningKey"]);
    }

    [Fact(DisplayName = "Array feature object supports direct settings")]
    public async Task ComposeAsync_ArrayObjectDirectSettings_AppearInConfigurationData()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:0:Name"] = "Analytics",
            ["Features:0:TopPostsCount"] = "10",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var settings = await bp.ComposeAsync();

        Assert.Equal(["Analytics"], settings.EnabledFeatures);
        Assert.Equal("10", settings.ConfigurationData["Analytics:TopPostsCount"]);
    }

    [Fact(DisplayName = "Array feature object rejects missing Name")]
    public async Task ComposeAsync_ArrayObjectMissingName_Throws()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:0:TopPostsCount"] = "10",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bp.ComposeAsync());
        Assert.Contains("Name", ex.Message);
        Assert.Contains("payments", ex.Message);
    }

    [Fact(DisplayName = "Array feature rejects blank string entry")]
    public async Task ComposeAsync_BlankArrayString_Throws()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:0"] = "",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bp.ComposeAsync());
        Assert.Contains("non-empty feature name", ex.Message);
        Assert.Contains("payments", ex.Message);
    }

    [Fact(DisplayName = "Array feature rejects mixed Settings wrapper and direct settings")]
    public async Task ComposeAsync_ArrayObjectMixedSettingsStyles_Throws()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:0:Name"] = "Analytics",
            ["Features:0:Settings:TopPostsCount"] = "10",
            ["Features:0:Window"] = "Weekly",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bp.ComposeAsync());
        Assert.Contains("mixes", ex.Message);
        Assert.Contains("Analytics", ex.Message);
    }

    [Fact(DisplayName = "Duplicate configured features throw before activation")]
    public async Task ComposeAsync_DuplicateFeatures_Throws()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:0"] = "Core",
            ["Features:1"] = "core",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bp.ComposeAsync());
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payments", ex.Message);
    }

    [Fact(DisplayName = "Configuration blueprint and ShellBuilder use equivalent feature normalization")]
    public async Task ComposeAsync_MatchesShellBuilderFeatureNormalization()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Shell:Features:Identity:Name"] = "Display Name",
            ["Shell:Features:Identity:SigningKey"] = "secret",
            ["Shell:Configuration:WebRouting:Path"] = "",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var section = root.GetSection("Shell");
        var bp = new ConfigurationShellBlueprint("payments", section);

        var blueprintSettings = await bp.ComposeAsync();
        var builderSettings = new ShellBuilder("payments")
            .FromConfiguration(section)
            .Build();

        Assert.Equal(builderSettings.EnabledFeatures, blueprintSettings.EnabledFeatures);
        Assert.Equal(builderSettings.ConfigurationData, blueprintSettings.ConfigurationData);
    }
}
