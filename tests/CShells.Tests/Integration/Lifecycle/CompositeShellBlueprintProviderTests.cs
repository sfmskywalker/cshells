using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
using CShells.Tests.TestHelpers;

namespace CShells.Tests.Integration.Lifecycle;

public class CompositeShellBlueprintProviderTests
{
    [Fact(DisplayName = "GetAsync routes to owning provider and short-circuits when duplicate-detection is off")]
    public async Task GetAsync_ShortCircuits_Disabled()
    {
        var a = new StubShellBlueprintProvider().Add("only-a");
        var b = new StubShellBlueprintProvider().Add("only-b");
        var c = new StubShellBlueprintProvider().Add("only-c");
        var composite = new CompositeShellBlueprintProvider(
            [a, b, c],
            new CompositeProviderOptions { DetectDuplicatesOnLookup = false });

        var hit = await composite.GetAsync("only-b");

        Assert.NotNull(hit);
        Assert.Equal(1, a.LookupCount);  // probed first
        Assert.Equal(1, b.LookupCount);  // hit
        Assert.Equal(0, c.LookupCount);  // short-circuited
    }

    [Fact(DisplayName = "GetAsync raises DuplicateBlueprintException when two providers claim the same name (detection on)")]
    public async Task GetAsync_Duplicate_Throws()
    {
        var a = new StubShellBlueprintProvider().Add("conflict");
        var b = new StubShellBlueprintProvider().Add("conflict");
        var composite = new CompositeShellBlueprintProvider(
            [a, b],
            new CompositeProviderOptions { DetectDuplicatesOnLookup = true });

        var ex = await Assert.ThrowsAsync<DuplicateBlueprintException>(
            () => composite.GetAsync("conflict"));

        Assert.Equal("conflict", ex.Name);
        Assert.Equal(typeof(StubShellBlueprintProvider), ex.FirstProviderType);
        Assert.Equal(typeof(StubShellBlueprintProvider), ex.SecondProviderType);
    }

    [Fact(DisplayName = "ListAsync across two providers yields every name exactly once across pages")]
    public async Task ListAsync_PagesAcrossProviders()
    {
        var a = new StubShellBlueprintProvider();
        for (var i = 0; i < 50; i++)
            a.Add($"a-{i:D2}");

        var b = new StubShellBlueprintProvider();
        for (var i = 0; i < 50; i++)
            b.Add($"b-{i:D2}");

        var composite = new CompositeShellBlueprintProvider([a, b]);

        var collected = new List<string>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var page = await composite.ListAsync(new BlueprintListQuery(Cursor: cursor, Limit: 10));
            collected.AddRange(page.Items.Select(i => i.Name));
            cursor = page.NextCursor;
            pageCount++;
            Assert.True(pageCount < 30, "Pagination did not converge");
        } while (cursor is not null);

        Assert.Equal(100, collected.Count);
        Assert.Equal(100, collected.Distinct().Count());  // no duplicates
    }

    [Fact(DisplayName = "ListAsync raises DuplicateBlueprintException when two providers contribute the same name within a single page")]
    public async Task ListAsync_IntraPageDuplicate_Throws()
    {
        // Both providers claim "conflict"; a single ListAsync call that merges both
        // sub-provider pages will observe the intra-page collision and raise.
        var a = new StubShellBlueprintProvider().Add("conflict").Add("a-only");
        var b = new StubShellBlueprintProvider().Add("conflict");

        var composite = new CompositeShellBlueprintProvider([a, b]);

        // Limit large enough to pull from both providers in one call.
        await Assert.ThrowsAsync<DuplicateBlueprintException>(
            () => composite.ListAsync(new BlueprintListQuery(Limit: 100)));
    }

    [Fact(DisplayName = "ListAsync resumes from NextCursor without gaps or duplicates")]
    public async Task ListAsync_CursorResumes_NoGapsOrDuplicates()
    {
        var a = new StubShellBlueprintProvider();
        foreach (var n in new[] { "a1", "a2", "a3", "a4" })
            a.Add(n);
        var b = new StubShellBlueprintProvider();
        foreach (var n in new[] { "b1", "b2", "b3" })
            b.Add(n);

        var composite = new CompositeShellBlueprintProvider([a, b]);

        var page1 = await composite.ListAsync(new BlueprintListQuery(Limit: 3));
        Assert.Equal(["a1", "a2", "a3"], page1.Items.Select(i => i.Name));
        Assert.NotNull(page1.NextCursor);

        var page2 = await composite.ListAsync(new BlueprintListQuery(Cursor: page1.NextCursor, Limit: 3));
        Assert.Equal(["a4", "b1", "b2"], page2.Items.Select(i => i.Name));
        Assert.NotNull(page2.NextCursor);

        var page3 = await composite.ListAsync(new BlueprintListQuery(Cursor: page2.NextCursor, Limit: 3));
        Assert.Equal(["b3"], page3.Items.Select(i => i.Name));
        Assert.Null(page3.NextCursor);
    }
}
