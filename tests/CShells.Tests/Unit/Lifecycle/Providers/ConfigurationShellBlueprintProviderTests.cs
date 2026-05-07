using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
using Microsoft.Extensions.Configuration;

namespace CShells.Tests.Unit.Lifecycle.Providers;

public class ConfigurationShellBlueprintProviderTests
{
    [Fact(DisplayName = "GetAsync returns the blueprint when the name exists as a child key")]
    public async Task GetAsync_MapKeyExists_ReturnsBlueprint()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Acme:Features:Core"] = "",
            ["Contoso:Features:Core"] = "",
        });

        var result = await provider.GetAsync("Acme");

        Assert.NotNull(result);
        Assert.Equal("Acme", result!.Blueprint.Name);
        Assert.Null(result.Manager);
    }

    [Fact(DisplayName = "GetAsync ignores shell-level Name as identity override")]
    public async Task GetAsync_ShellLevelNameDiffersFromMapKey_UsesMapKeyOnly()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Default:Name"] = "Renamed",
            ["Default:Features:Core"] = "",
        });

        var byMapKey = await provider.GetAsync("Default");
        var byInnerName = await provider.GetAsync("Renamed");

        Assert.NotNull(byMapKey);
        Assert.Equal("Default", byMapKey!.Blueprint.Name);
        Assert.Null(byInnerName);
    }

    [Fact(DisplayName = "GetAsync is case-insensitive")]
    public async Task GetAsync_NameDiffersByCase_ReturnsBlueprint()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ACME:Features:Core"] = "",
        });

        var lower = await provider.GetAsync("acme");

        Assert.NotNull(lower);
    }

    [Fact(DisplayName = "GetAsync returns null for unknown name")]
    public async Task GetAsync_UnknownName_ReturnsNull()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Present:Features:Core"] = "",
        });

        var result = await provider.GetAsync("Missing");

        Assert.Null(result);
    }

    [Fact(DisplayName = "ListAsync returns children sorted by map key; all entries are Mutable=false")]
    public async Task ListAsync_MapKeys_ReturnsSortedReadOnlyBlueprints()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["c:Features:Core"] = "",
            ["a:Features:Core"] = "",
            ["b:Features:Core"] = "",
        });

        var page = await provider.ListAsync(new BlueprintListQuery(Limit: 10));

        Assert.Equal(["a", "b", "c"], page.Items.Select(i => i.Name));
        Assert.All(page.Items, i => Assert.False(i.Mutable));
        Assert.All(page.Items, i => Assert.Equal(ConfigurationShellBlueprintProvider.SourceIdValue, i.SourceId));
    }

    [Fact(DisplayName = "ListAsync paginates with Limit and NextCursor")]
    public async Task ListAsync_MultipleMapKeys_Paginates()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["a:Features:Core"] = "",
            ["b:Features:Core"] = "",
            ["c:Features:Core"] = "",
            ["d:Features:Core"] = "",
            ["e:Features:Core"] = "",
        });

        var first = await provider.ListAsync(new BlueprintListQuery(Limit: 2));
        var second = await provider.ListAsync(new BlueprintListQuery(Cursor: first.NextCursor, Limit: 2));
        var third = await provider.ListAsync(new BlueprintListQuery(Cursor: second.NextCursor, Limit: 2));

        Assert.Equal(["a", "b"], first.Items.Select(i => i.Name));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["c", "d"], second.Items.Select(i => i.Name));
        Assert.Equal(["e"], third.Items.Select(i => i.Name));
        Assert.Null(third.NextCursor);
    }

    [Fact(DisplayName = "GetAsync rejects numeric shell children as unsupported array syntax")]
    public async Task GetAsync_NumericChildKey_Throws()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["0:Name"] = "Acme",
            ["0:Features:Core"] = "",
            ["Default:Features:Core"] = "",
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync("Default"));

        Assert.Contains("CShells:Shells", ex.Message);
        Assert.Contains("array syntax", ex.Message);
        Assert.Contains("named map entries", ex.Message);
    }

    [Fact(DisplayName = "ListAsync rejects numeric shell children as unsupported array syntax")]
    public async Task ListAsync_NumericChildKey_Throws()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["0:Features:Core"] = "",
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ListAsync(new BlueprintListQuery(Limit: 10)));

        Assert.Contains("CShells:Shells", ex.Message);
        Assert.Contains("array syntax", ex.Message);
    }

    [Fact(DisplayName = "Named override targets only the requested shell feature setting")]
    public async Task ComposeAsync_NamedOverride_TargetsOnlyRequestedShell()
    {
        var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Default:Features:Identity:SigningKey"] = "default-base",
                ["Contoso:Features:Identity:SigningKey"] = "contoso-base",
            },
            new Dictionary<string, string?>
            {
                ["CSHELLS:SHELLS:DEFAULT:FEATURES:IDENTITY:SIGNINGKEY"] = "test",
            });

        var defaultSettings = await (await provider.GetAsync("Default"))!.Blueprint.ComposeAsync();
        var contosoSettings = await (await provider.GetAsync("Contoso"))!.Blueprint.ComposeAsync();

        Assert.Equal("test", defaultSettings.ConfigurationData["Identity:SigningKey"]);
        Assert.Equal("contoso-base", contosoSettings.ConfigurationData["Identity:SigningKey"]);
    }

    [Fact(DisplayName = "Named lookup is stable when shell entries are reordered")]
    public async Task ComposeAsync_ReorderedShellEntries_NamedLookupRemainsStable()
    {
        var firstOrder = BuildProvider(new Dictionary<string, string?>
        {
            ["Default:Features:Identity:SigningKey"] = "default",
            ["Contoso:Features:Identity:SigningKey"] = "contoso",
        });
        var secondOrder = BuildProvider(new Dictionary<string, string?>
        {
            ["Contoso:Features:Identity:SigningKey"] = "contoso",
            ["Default:Features:Identity:SigningKey"] = "default",
        });

        var firstSettings = await (await firstOrder.GetAsync("Default"))!.Blueprint.ComposeAsync();
        var secondSettings = await (await secondOrder.GetAsync("Default"))!.Blueprint.ComposeAsync();

        Assert.Equal("default", firstSettings.ConfigurationData["Identity:SigningKey"]);
        Assert.Equal("default", secondSettings.ConfigurationData["Identity:SigningKey"]);
    }

    [Fact(DisplayName = "Layered configuration overrides named shell values while preserving unaffected settings")]
    public async Task ComposeAsync_LayeredConfiguration_MergesByShellName()
    {
        var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Default:Configuration:Plan"] = "Free",
                ["Default:Configuration:WebRouting:Path"] = "",
                ["Default:Features:Identity:SigningKey"] = "base",
            },
            new Dictionary<string, string?>
            {
                ["CShells:Shells:Default:Configuration:Plan"] = "Enterprise",
            });

        var settings = await (await provider.GetAsync("Default"))!.Blueprint.ComposeAsync();

        Assert.Equal("Enterprise", settings.ConfigurationData["Plan"]);
        Assert.Equal("", settings.ConfigurationData["WebRouting:Path"]);
        Assert.Equal("base", settings.ConfigurationData["Identity:SigningKey"]);
    }

    [Fact(DisplayName = "Layered configuration adds new named shells")]
    public async Task ListAsync_LayeredConfiguration_AddsNamedShells()
    {
        var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Default:Features:Identity:SigningKey"] = "default",
            },
            new Dictionary<string, string?>
            {
                ["CShells:Shells:Contoso:Configuration:WebRouting:Path"] = "contoso",
                ["CShells:Shells:Contoso:Features:Identity"] = "",
            });

        var page = await provider.ListAsync(new BlueprintListQuery(Limit: 10));

        Assert.Equal(["Contoso", "Default"], page.Items.Select(i => i.Name));
    }

    private static ConfigurationShellBlueprintProvider BuildProvider(params IDictionary<string, string?>[] layers)
    {
        var builder = new ConfigurationBuilder();
        foreach (var layer in layers)
            builder.AddInMemoryCollection(AddShellsPrefix(layer));

        return new ConfigurationShellBlueprintProvider(
            builder.Build().GetSection("CShells:Shells"));
    }

    private static Dictionary<string, string?> AddShellsPrefix(IDictionary<string, string?> pairs) =>
        pairs.ToDictionary(
            pair => pair.Key.StartsWith("CShells:", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.StartsWith("CSHELLS:", StringComparison.OrdinalIgnoreCase)
                ? pair.Key
                : $"CShells:Shells:{pair.Key}",
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
}
