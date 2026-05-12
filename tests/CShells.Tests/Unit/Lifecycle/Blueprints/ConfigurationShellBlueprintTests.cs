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

    [Fact(DisplayName = "ComposeAsync uses blueprint name and loads map feature settings with shell configuration")]
    public async Task ComposeAsync_MapFeatureSettingsAndShellConfiguration_LoadsRuntimeSettings()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:Identity:SigningKey"] = "secret",
            ["Configuration:WebRouting:Path"] = "",
            ["Configuration:Plan"] = "Enterprise",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("Default", root);

        var settings = await bp.ComposeAsync();

        Assert.Equal("Default", settings.Id.Name);
        Assert.Equal(["Identity"], settings.EnabledFeatures);
        Assert.Equal("secret", settings.ConfigurationData["Identity:SigningKey"]);
        Assert.Equal("", settings.ConfigurationData["WebRouting:Path"]);
        Assert.Equal("Enterprise", settings.ConfigurationData["Plan"]);
    }

    [Fact(DisplayName = "ComposeAsync enables compact true map features")]
    public async Task ComposeAsync_CompactTrueMapFeature_EnablesFeatureWithDefaults()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:Core"] = "true",
            ["Features:Posts"] = "TRUE",
            ["Features:Http:BasePath"] = "/workflows",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("Default", root);

        var settings = await bp.ComposeAsync();

        Assert.Equal(["Core", "Http", "Posts"], settings.EnabledFeatures.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["Core", "Posts"], settings.FeatureSettingResets.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("/workflows", settings.ConfigurationData["Http:BasePath"]);
    }

    [Fact(DisplayName = "ComposeAsync disables map feature and ignores inherited child settings")]
    public async Task ComposeAsync_DisabledMapFeature_RemovesFeatureAndSettings()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:Identity"] = "false",
            ["Features:Identity:SigningKey"] = "secret",
            ["Configuration:Identity:SigningKey"] = "configuration-secret",
            ["Features:Http:BasePath"] = "/workflows",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("Default", root);

        var settings = await bp.ComposeAsync();

        Assert.Equal(["Http"], settings.EnabledFeatures);
        Assert.Equal(["Identity"], settings.DisabledFeatures);
        Assert.False(settings.ConfigurationData.ContainsKey("Identity:SigningKey"));
        Assert.Equal("/workflows", settings.ConfigurationData["Http:BasePath"]);
    }

    [Fact(DisplayName = "ComposeAsync later true re-enables disabled feature")]
    public async Task ComposeAsync_LaterTrue_ReEnablesDisabledFeature()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:Identity"] = "false",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:Identity"] = "true",
            })
            .Build();
        var bp = new ConfigurationShellBlueprint("Default", root);

        var settings = await bp.ComposeAsync();

        Assert.Equal(["Identity"], settings.EnabledFeatures);
        Assert.Empty(settings.DisabledFeatures);
        Assert.Equal(["Identity"], settings.FeatureSettingResets);
    }

    [Fact(DisplayName = "ComposeAsync object feature enables feature with settings")]
    public async Task ComposeAsync_ObjectFeature_EnablesFeatureWithSettings()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:Identity:SigningKey"] = "configured",
            })
            .Build();
        var bp = new ConfigurationShellBlueprint("Default", root);

        var settings = await bp.ComposeAsync();

        Assert.Equal(["Identity"], settings.EnabledFeatures);
        Assert.Empty(settings.DisabledFeatures);
        Assert.Empty(settings.FeatureSettingResets);
        Assert.Equal("configured", settings.ConfigurationData["Identity:SigningKey"]);
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

    [Fact(DisplayName = "Array feature reports scalar Settings before mixed styles")]
    public async Task ComposeAsync_ArrayObjectScalarSettingsWrapperAndDirectSettings_ReportsScalarSettings()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Features:0:Name"] = "Analytics",
            ["Features:0:Settings"] = "invalid",
            ["Features:0:Window"] = "Weekly",
        };
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var bp = new ConfigurationShellBlueprint("payments", root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bp.ComposeAsync());
        Assert.Contains("Settings", ex.Message);
        Assert.Contains("object", ex.Message);
        Assert.DoesNotContain("mixes", ex.Message);
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
