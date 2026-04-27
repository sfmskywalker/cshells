using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells.Management.Api.Models;
using CShells.Tests.TestHelpers;

namespace CShells.Tests.Integration.Management;

/// <summary>
/// Integration tests for <c>POST /reload-all</c> (US2 + US6). Covers the batch success path,
/// partial-failure handling, the empty-active-set edge case, and the parallelism query
/// validation (US6 cases live alongside the US2 cases in this single file per the task plan).
/// </summary>
public class ReloadAllEndpointTests
{
    [Fact(DisplayName = "Reload-all with three active shells returns 200 with three advanced entries")]
    public async Task ReloadAll_ThreeActiveShells_Returns200_WithThreeAdvancedEntries()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("alpha", _ => { })
            .AddShell("beta", _ => { })
            .AddShell("gamma", _ => { }));

        // Activate all three.
        foreach (var name in new[] { "alpha", "beta", "gamma" })
            await fixture.Registry.GetOrActivateAsync(name);

        var response = await fixture.PostAsync("/admin/reload-all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReloadResultResponse[]>(WebJson);
        Assert.NotNull(body);
        Assert.Equal(3, body.Length);
        Assert.All(body, entry =>
        {
            Assert.True(entry.Success, $"Reload of '{entry.Name}' was unsuccessful: {entry.Error?.Message}");
            Assert.NotNull(entry.NewShell);
            Assert.Equal(2, entry.NewShell.Generation);
            Assert.Null(entry.Error);
        });
        Assert.Equal(["alpha", "beta", "gamma"], body.Select(b => b.Name).OrderBy(s => s).ToArray());
    }

    [Fact(DisplayName = "Reload-all with one shell's blueprint provider failing returns 200 with mixed entries")]
    public async Task ReloadAll_OneShellFails_OthersStillReload_StatusStill200()
    {
        var stub = new ThrowOnSpecificNameProvider("broken")
            .Add("good-1")
            .Add("good-2")
            .Add("broken");

        await using var fixture = new ManagementApiFixture(c => c.AddBlueprintProvider(_ => stub));

        // Activate all three (the throwing-name guard isn't engaged yet).
        foreach (var name in new[] { "good-1", "good-2", "broken" })
            await fixture.Registry.GetOrActivateAsync(name);

        // Engage the failure for subsequent lookups.
        stub.ThrowForName = true;

        var response = await fixture.PostAsync("/admin/reload-all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReloadResultResponse[]>(WebJson);
        Assert.NotNull(body);
        Assert.Equal(3, body.Length);

        var byName = body.ToDictionary(b => b.Name);
        Assert.True(byName["good-1"].Success);
        Assert.True(byName["good-2"].Success);
        Assert.False(byName["broken"].Success);
        Assert.NotNull(byName["broken"].Error);
        Assert.Equal("ShellBlueprintUnavailableException", byName["broken"].Error!.Type);
    }

    [Fact(DisplayName = "Reload-all with no active shells returns 200 with an empty array")]
    public async Task ReloadAll_NoActiveShells_Returns200_WithEmptyArray()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("registered-but-never-activated", _ => { }));

        var response = await fixture.PostAsync("/admin/reload-all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReloadResultResponse[]>(WebJson);
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    // =========================================================================
    // US6: Parallelism query validation
    // =========================================================================

    [Fact(DisplayName = "Reload-all with parallelism=1 returns 200 (US6)")]
    public async Task ReloadAll_WithParallelism1_Returns200()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("a", _ => { })
            .AddShell("b", _ => { }));
        foreach (var name in new[] { "a", "b" })
            await fixture.Registry.GetOrActivateAsync(name);

        var response = await fixture.PostAsync("/admin/reload-all?maxDegreeOfParallelism=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(DisplayName = "Reload-all with parallelism out of range returns 400 (US6)")]
    [InlineData(0)]
    [InlineData(65)]
    public async Task ReloadAll_WithParallelismOutOfRange_Returns400(int parallelism)
    {
        await using var fixture = new ManagementApiFixture(c => c.AddShell("a", _ => { }));

        var response = await fixture.PostAsync($"/admin/reload-all?maxDegreeOfParallelism={parallelism}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(WebJson);
        var detail = problem.GetProperty("detail").GetString() ?? "";
        Assert.Contains("MaxDegreeOfParallelism", detail);
    }

    [Fact(DisplayName = "Reload-all with non-integer parallelism returns 400 (US6)")]
    public async Task ReloadAll_WithParallelismAbc_Returns400()
    {
        await using var fixture = new ManagementApiFixture(c => c.AddShell("a", _ => { }));

        var response = await fixture.PostAsync("/admin/reload-all?maxDegreeOfParallelism=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Reload-all without parallelism query uses the default (US6)")]
    public async Task ReloadAll_NoParallelismQuery_UsesDefault8()
    {
        // Indirect verification: with no query string, the endpoint must not fail (which would
        // have happened if it tried to construct ReloadOptions(0) etc.). The default of 8 is
        // documented on ReloadOptions; this test ensures the absence of the query param is
        // handled cleanly.
        await using var fixture = new ManagementApiFixture(c => c.AddShell("a", _ => { }));
        await fixture.Registry.GetOrActivateAsync("a");

        var response = await fixture.PostAsync("/admin/reload-all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stub provider that fails <see cref="IShellBlueprintProvider.GetAsync"/> only for a
    /// configured name when <see cref="ThrowForName"/> is true. Wraps a
    /// <see cref="StubShellBlueprintProvider"/> for catalogue + non-failing lookups.
    /// </summary>
    private sealed class ThrowOnSpecificNameProvider(string brokenName) : CShells.Lifecycle.IShellBlueprintProvider
    {
        private readonly StubShellBlueprintProvider _inner = new();

        public bool ThrowForName { get; set; }

        public ThrowOnSpecificNameProvider Add(string name)
        {
            _inner.Add(name);
            return this;
        }

        public Task<CShells.Lifecycle.ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            if (ThrowForName && string.Equals(name, brokenName, StringComparison.OrdinalIgnoreCase))
                throw new ApplicationException($"source unreachable for '{name}': simulated");
            return _inner.GetAsync(name, cancellationToken);
        }

        public Task<CShells.Lifecycle.BlueprintPage> ListAsync(CShells.Lifecycle.BlueprintListQuery query, CancellationToken cancellationToken = default)
            => _inner.ListAsync(query, cancellationToken);
    }
}
