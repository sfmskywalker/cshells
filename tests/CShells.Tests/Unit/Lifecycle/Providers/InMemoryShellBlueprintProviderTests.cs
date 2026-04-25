using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using CShells.Lifecycle.Providers;

namespace CShells.Tests.Unit.Lifecycle.Providers;

public class InMemoryShellBlueprintProviderTests
{
    [Fact(DisplayName = "Add + GetAsync returns the blueprint paired with no manager by default")]
    public async Task Add_Get_NoManager()
    {
        var provider = new InMemoryShellBlueprintProvider();
        var bp = new DelegateShellBlueprint("payments", _ => { });
        provider.Add(bp);

        var result = await provider.GetAsync("payments");

        Assert.NotNull(result);
        Assert.Same(bp, result!.Blueprint);
        Assert.Null(result.Manager);
    }

    [Fact(DisplayName = "GetAsync is case-insensitive")]
    public async Task Get_CaseInsensitive()
    {
        var provider = new InMemoryShellBlueprintProvider();
        provider.Add(new DelegateShellBlueprint("PaymentS", _ => { }));

        var lower = await provider.GetAsync("payments");
        var upper = await provider.GetAsync("PAYMENTS");

        Assert.NotNull(lower);
        Assert.NotNull(upper);
    }

    [Fact(DisplayName = "GetAsync returns null for unknown name")]
    public async Task Get_Unknown_ReturnsNull()
    {
        var provider = new InMemoryShellBlueprintProvider();
        provider.Add(new DelegateShellBlueprint("a", _ => { }));

        var result = await provider.GetAsync("b");

        Assert.Null(result);
    }

    [Fact(DisplayName = "Add + manager sets Mutable=true on the summary")]
    public async Task Add_WithManager_MutableSummary()
    {
        var provider = new InMemoryShellBlueprintProvider();
        var manager = new TestHelpers.StubShellBlueprintManager();
        provider.Add(new DelegateShellBlueprint("tenant-x", _ => { }), manager);

        var page = await provider.ListAsync(new BlueprintListQuery());

        Assert.Single(page.Items);
        Assert.True(page.Items[0].Mutable);
    }

    [Fact(DisplayName = "Duplicate Add throws")]
    public void Duplicate_Add_Throws()
    {
        var provider = new InMemoryShellBlueprintProvider();
        provider.Add(new DelegateShellBlueprint("a", _ => { }));

        Assert.Throws<InvalidOperationException>(() =>
            provider.Add(new DelegateShellBlueprint("A", _ => { })));  // case-insensitive clash
    }

    [Fact(DisplayName = "ListAsync returns entries sorted by name and paginates correctly")]
    public async Task List_SortsAndPaginates()
    {
        var provider = new InMemoryShellBlueprintProvider();
        foreach (var n in new[] { "c", "a", "b", "d", "e" })
            provider.Add(new DelegateShellBlueprint(n, _ => { }));

        var first = await provider.ListAsync(new BlueprintListQuery(Limit: 2));
        Assert.Equal(["a", "b"], first.Items.Select(i => i.Name));
        Assert.NotNull(first.NextCursor);

        var second = await provider.ListAsync(new BlueprintListQuery(Cursor: first.NextCursor, Limit: 2));
        Assert.Equal(["c", "d"], second.Items.Select(i => i.Name));
        Assert.NotNull(second.NextCursor);

        var third = await provider.ListAsync(new BlueprintListQuery(Cursor: second.NextCursor, Limit: 2));
        Assert.Equal(["e"], third.Items.Select(i => i.Name));
        Assert.Null(third.NextCursor);
    }

    [Fact(DisplayName = "ListAsync respects NamePrefix filter")]
    public async Task List_NamePrefix_Filters()
    {
        var provider = new InMemoryShellBlueprintProvider();
        foreach (var n in new[] { "app-1", "app-2", "infra-1", "infra-2" })
            provider.Add(new DelegateShellBlueprint(n, _ => { }));

        var page = await provider.ListAsync(new BlueprintListQuery(NamePrefix: "app-"));

        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.StartsWith("app-", i.Name));
    }

    [Fact(DisplayName = "ExistsAsync is true for known names, false otherwise")]
    public async Task Exists_KnownUnknown()
    {
        var provider = new InMemoryShellBlueprintProvider();
        provider.Add(new DelegateShellBlueprint("present", _ => { }));

        Assert.True(await provider.ExistsAsync("present"));
        Assert.False(await provider.ExistsAsync("missing"));
    }

    [Fact(DisplayName = "Empty provider: ListAsync returns empty page, null cursor")]
    public async Task Empty_ReturnsEmptyPage()
    {
        var provider = new InMemoryShellBlueprintProvider();

        var page = await provider.ListAsync(new BlueprintListQuery());

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact(DisplayName = "ListAsync validates Limit range")]
    public async Task List_Limit_Guarded()
    {
        var provider = new InMemoryShellBlueprintProvider();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.ListAsync(new BlueprintListQuery(Limit: 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.ListAsync(new BlueprintListQuery(Limit: 501)));
    }
}
