using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryListTests
{
    [Fact(DisplayName = "ListAsync pages a catalogue of 1000 with Limit=100 → 10 pages, each name once")]
    public async Task ListAsync_Pages1000_In10PagesOf100()
    {
        // 008: single-provider model. The 1000-entry catalogue is served by one provider
        // (previously split across two stubs in the 007 multi-provider test).
        var provider = new StubShellBlueprintProvider();
        for (var i = 0; i < 500; i++)
            provider.Add($"a-{i:D4}");
        for (var i = 0; i < 500; i++)
            provider.Add($"b-{i:D4}");

        await using var host = BuildHostWith(provider);
        var registry = host.GetRequiredService<IShellRegistry>();

        var allNames = new List<string>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var page = await registry.ListAsync(new ShellListQuery(Cursor: cursor, Limit: 100));
            Assert.True(page.Items.Count <= 100);
            allNames.AddRange(page.Items.Select(i => i.Name));
            cursor = page.NextCursor;
            pageCount++;
            Assert.True(pageCount < 20, "Pagination did not converge");
        } while (cursor is not null);

        Assert.Equal(1000, allNames.Count);
        Assert.Equal(1000, allNames.Distinct().Count());
        Assert.Equal(10, pageCount);
    }

    [Fact(DisplayName = "ListAsync with NamePrefix filters out non-matching names")]
    public async Task ListAsync_NamePrefix_Filters()
    {
        var a = new StubShellBlueprintProvider();
        foreach (var n in new[] { "tenant-a1", "tenant-a2", "infra-x", "infra-y" })
            a.Add(n);

        await using var host = BuildHostWith(a);
        var registry = host.GetRequiredService<IShellRegistry>();

        var page = await registry.ListAsync(new ShellListQuery(NamePrefix: "tenant-"));

        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.StartsWith("tenant-", i.Name));
    }

    [Fact(DisplayName = "ListAsync left-joins lifecycle state: inactive blueprints → null lifecycle fields; active → populated")]
    public async Task ListAsync_LeftJoinsLifecycleState()
    {
        var a = new StubShellBlueprintProvider()
            .Add("hot")
            .Add("cold");

        await using var host = BuildHostWith(a);
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.GetOrActivateAsync("hot");

        var page = await registry.ListAsync(new ShellListQuery(Limit: 10));

        var hot = page.Items.Single(i => i.Name == "hot");
        Assert.Equal(1, hot.ActiveGeneration);
        Assert.Equal(ShellLifecycleState.Active, hot.State);

        var cold = page.Items.Single(i => i.Name == "cold");
        Assert.Null(cold.ActiveGeneration);
        Assert.Null(cold.State);
        Assert.Equal(0, cold.ActiveScopeCount);
    }

    [Fact(DisplayName = "ListAsync with StateFilter filters to only shells in that state; inactive blueprints are excluded")]
    public async Task ListAsync_StateFilter_FiltersInactiveOut()
    {
        var a = new StubShellBlueprintProvider()
            .Add("active-1")
            .Add("active-2")
            .Add("inactive");

        await using var host = BuildHostWith(a);
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.GetOrActivateAsync("active-1");
        await registry.GetOrActivateAsync("active-2");

        var page = await registry.ListAsync(new ShellListQuery(StateFilter: ShellLifecycleState.Active));

        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.Equal(ShellLifecycleState.Active, i.State));
        Assert.DoesNotContain(page.Items, i => i.Name == "inactive");
    }

    private static ServiceProvider BuildHostWith(params IShellBlueprintProvider[] providers)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCShells(cshells =>
        {
            cshells.WithAssemblies();
            foreach (var p in providers)
                cshells.AddBlueprintProvider(_ => p);
        });
        return services.BuildServiceProvider();
    }
}
