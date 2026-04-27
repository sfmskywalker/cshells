using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells.Lifecycle;
using CShells.Management.Api.Models;

namespace CShells.Tests.Integration.Management;

/// <summary>
/// Integration tests for <c>GET /</c> (US3 list portion). Covers paged listing across
/// active/inactive shells, page-size validation, and empty-registry edge case.
/// </summary>
public class ListShellsEndpointTests
{
    [Fact(DisplayName = "List with five blueprints (three active) returns 200 with five items")]
    public async Task List_FiveBlueprintsThreeActive_Returns200_WithFiveItems()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("a", _ => { })
            .AddShell("b", _ => { })
            .AddShell("c", _ => { })
            .AddShell("d", _ => { })
            .AddShell("e", _ => { }));

        // Activate three of the five.
        foreach (var name in new[] { "a", "b", "c" })
            await fixture.Registry.GetOrActivateAsync(name);

        var page = await fixture.GetJsonAsync<ShellPageResponse>("/admin/");
        Assert.NotNull(page);
        Assert.Equal(5, page.Items.Count);

        var byName = page.Items.ToDictionary(i => i.Name);
        Assert.NotNull(byName["a"].Active);
        Assert.NotNull(byName["b"].Active);
        Assert.NotNull(byName["c"].Active);
        Assert.Null(byName["d"].Active);
        Assert.Null(byName["e"].Active);
    }

    [Fact(DisplayName = "List with pageSize=2 returns two items and a next cursor")]
    public async Task List_PageSize2_Returns2ItemsAndCursor()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("a", _ => { })
            .AddShell("b", _ => { })
            .AddShell("c", _ => { }));

        var page = await fixture.GetJsonAsync<ShellPageResponse>("/admin/?pageSize=2");
        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
        Assert.NotNull(page.NextCursor);
        Assert.Equal(2, page.PageSize);
    }

    [Fact(DisplayName = "List with pageSize out of range returns 400")]
    public async Task List_PageSizeOutOfRange_Returns400()
    {
        await using var fixture = new ManagementApiFixture(c => c.AddShell("a", _ => { }));

        var response = await fixture.GetAsync("/admin/?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "List with empty registry returns 200 with empty items")]
    public async Task List_EmptyRegistry_Returns200_EmptyArray()
    {
        await using var fixture = new ManagementApiFixture();

        var page = await fixture.GetJsonAsync<ShellPageResponse>("/admin/");
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact(DisplayName = "List degrades gracefully when one blueprint source fails (Greptile PR-91 P1)")]
    public async Task List_OneBlueprintFails_StillReturns200_WithNullBlueprintForFailingRow()
    {
        var stub = new SelectivelyFailingProvider(failOnName: "broken")
            .Add("good-1")
            .Add("broken")
            .Add("good-2");

        await using var fixture = new ManagementApiFixture(c => c.AddBlueprintProvider(_ => stub));

        var page = await fixture.GetJsonAsync<ShellPageResponse>("/admin/");
        Assert.NotNull(page);
        Assert.Equal(3, page.Items.Count);

        var byName = page.Items.ToDictionary(i => i.Name);
        Assert.NotNull(byName["good-1"].Blueprint);
        Assert.NotNull(byName["good-2"].Blueprint);
        Assert.Null(byName["broken"].Blueprint); // graceful null, not an aborted page
    }

    /// <summary>
    /// Returns a blueprint for every name except <c>failOnName</c>, which surfaces as a
    /// <see cref="ShellBlueprintUnavailableException"/> via the registry's wrap-on-throw.
    /// </summary>
    private sealed class SelectivelyFailingProvider(string failOnName) : IShellBlueprintProvider
    {
        private readonly TestHelpers.StubShellBlueprintProvider _inner = new();

        public SelectivelyFailingProvider Add(string name)
        {
            _inner.Add(name);
            return this;
        }

        public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.Equals(name, failOnName, StringComparison.OrdinalIgnoreCase))
                throw new ApplicationException($"blueprint source unreachable for '{name}'");
            return _inner.GetAsync(name, cancellationToken);
        }

        public Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default)
            => _inner.ListAsync(query, cancellationToken);
    }
}
