using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
}
