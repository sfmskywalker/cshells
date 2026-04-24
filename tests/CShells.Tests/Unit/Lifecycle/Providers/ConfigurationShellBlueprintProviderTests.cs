using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
using Microsoft.Extensions.Configuration;

namespace CShells.Tests.Unit.Lifecycle.Providers;

public class ConfigurationShellBlueprintProviderTests
{
    [Fact(DisplayName = "GetAsync returns the blueprint when the name exists as a child key")]
    public async Task Get_ByKey()
    {
        var section = BuildSection(new Dictionary<string, string?>
        {
            ["acme:Features:0"] = "Core",
            ["contoso:Features:0"] = "Core",
        });
        var provider = new ConfigurationShellBlueprintProvider(section);

        var result = await provider.GetAsync("acme");

        Assert.NotNull(result);
        Assert.Equal("acme", result!.Blueprint.Name);
        Assert.Null(result.Manager);
    }

    [Fact(DisplayName = "GetAsync returns the blueprint when the child declares an explicit Name")]
    public async Task Get_ByExplicitName()
    {
        var section = BuildSection(new Dictionary<string, string?>
        {
            ["t1:Name"] = "Acme",
            ["t2:Name"] = "Contoso",
        });
        var provider = new ConfigurationShellBlueprintProvider(section);

        var result = await provider.GetAsync("Acme");

        Assert.NotNull(result);
        Assert.Equal("Acme", result!.Blueprint.Name);
    }

    [Fact(DisplayName = "GetAsync is case-insensitive")]
    public async Task Get_CaseInsensitive()
    {
        var section = BuildSection(new Dictionary<string, string?>
        {
            ["ACME:Features:0"] = "Core",
        });
        var provider = new ConfigurationShellBlueprintProvider(section);

        var lower = await provider.GetAsync("acme");

        Assert.NotNull(lower);
    }

    [Fact(DisplayName = "GetAsync returns null for unknown name")]
    public async Task Get_Unknown_ReturnsNull()
    {
        var section = BuildSection(new Dictionary<string, string?>
        {
            ["present:Features:0"] = "Core",
        });
        var provider = new ConfigurationShellBlueprintProvider(section);

        var result = await provider.GetAsync("missing");

        Assert.Null(result);
    }

    [Fact(DisplayName = "ListAsync returns children sorted by name; all entries are Mutable=false")]
    public async Task List_SortedReadOnly()
    {
        var section = BuildSection(new Dictionary<string, string?>
        {
            ["c:Features:0"] = "Core",
            ["a:Features:0"] = "Core",
            ["b:Features:0"] = "Core",
        });
        var provider = new ConfigurationShellBlueprintProvider(section);

        var page = await provider.ListAsync(new BlueprintListQuery(Limit: 10));

        Assert.Equal(["a", "b", "c"], page.Items.Select(i => i.Name));
        Assert.All(page.Items, i => Assert.False(i.Mutable));
        Assert.All(page.Items, i => Assert.Equal(ConfigurationShellBlueprintProvider.SourceIdValue, i.SourceId));
    }

    [Fact(DisplayName = "ListAsync paginates with Limit and NextCursor")]
    public async Task List_Paginates()
    {
        var section = BuildSection(new Dictionary<string, string?>
        {
            ["a:Features:0"] = "Core",
            ["b:Features:0"] = "Core",
            ["c:Features:0"] = "Core",
            ["d:Features:0"] = "Core",
            ["e:Features:0"] = "Core",
        });
        var provider = new ConfigurationShellBlueprintProvider(section);

        var first = await provider.ListAsync(new BlueprintListQuery(Limit: 2));
        Assert.Equal(["a", "b"], first.Items.Select(i => i.Name));
        Assert.NotNull(first.NextCursor);

        var second = await provider.ListAsync(new BlueprintListQuery(Cursor: first.NextCursor, Limit: 2));
        Assert.Equal(["c", "d"], second.Items.Select(i => i.Name));

        var third = await provider.ListAsync(new BlueprintListQuery(Cursor: second.NextCursor, Limit: 2));
        Assert.Equal(["e"], third.Items.Select(i => i.Name));
        Assert.Null(third.NextCursor);
    }

    private static IConfiguration BuildSection(IDictionary<string, string?> pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
}
