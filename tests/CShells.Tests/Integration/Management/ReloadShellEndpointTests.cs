using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells.Lifecycle;
using CShells.Management.Api.Models;
using CShells.Tests.TestHelpers;

namespace CShells.Tests.Integration.Management;

/// <summary>
/// Integration tests for <c>POST /reload/{name}</c> (US1). Covers the success path with
/// drain snapshot, plus the FR-013 error mappings (404 not-found and 503 unavailable).
/// </summary>
public class ReloadShellEndpointTests
{
    [Fact(DisplayName = "Reload of a known active shell returns 200 with advanced generation and drain snapshot")]
    public async Task Reload_KnownActiveShell_Returns200_WithAdvancedGeneration()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("acme", _ => { }));

        // Pre-activate so reload sees a previous generation.
        await fixture.Registry.GetOrActivateAsync("acme");

        var response = await fixture.PostAsync("/admin/reload/acme");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReloadResultResponse>(WebJson);
        Assert.NotNull(body);
        Assert.Equal("acme", body.Name);
        Assert.True(body.Success);
        Assert.NotNull(body.NewShell);
        Assert.Equal(2, body.NewShell.Generation);
        Assert.Equal(ShellLifecycleState.Active.ToString(), body.NewShell.State);
        Assert.NotNull(body.Drain);
        Assert.Contains(body.Drain.Status, new[] { "Pending", "Completed", "Forced", "TimedOut" });
        Assert.Null(body.Error);
    }

    [Fact(DisplayName = "Reload of an unknown name returns 404 with problem-details")]
    public async Task Reload_UnknownName_Returns404_WithProblemDetails()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("known", _ => { }));

        var response = await fixture.PostAsync("/admin/reload/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(WebJson);
        Assert.Equal("Not Found", problem.GetProperty("title").GetString());
        Assert.Contains("does-not-exist", problem.GetProperty("detail").GetString() ?? "");
    }

    [Fact(DisplayName = "Reload when the blueprint provider throws returns 503 with problem-details")]
    public async Task Reload_ProviderUnavailable_Returns503()
    {
        var stub = new StubShellBlueprintProvider().Add("acme");
        await using var fixture = new ManagementApiFixture(c => c.AddBlueprintProvider(_ => stub));

        // Activate once successfully so the registry has gen 1 in flight.
        await fixture.Registry.GetOrActivateAsync("acme");

        // Now break the provider; the next reload's blueprint lookup will throw and the
        // registry wraps it as ShellBlueprintUnavailableException.
        stub.ThrowOnGet = new ApplicationException("source unreachable: simulated");

        var response = await fixture.PostAsync("/admin/reload/acme");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "Reload during host-shutdown cancellation returns 503 with problem-details")]
    public async Task Reload_HostShutdownCancellation_Returns503()
    {
        // The registry's ShouldWrapAsUnavailable explicitly excludes OperationCanceledException
        // from the Unavailable wrap, so a provider that throws OCE propagates it raw — and the
        // management API's ResultMapper maps OCE → 503 per FR-013.
        var stub = new StubShellBlueprintProvider().Add("acme");
        await using var fixture = new ManagementApiFixture(c => c.AddBlueprintProvider(_ => stub));
        await fixture.Registry.GetOrActivateAsync("acme");

        stub.ThrowOnGet = new OperationCanceledException("simulated host shutdown");

        var response = await fixture.PostAsync("/admin/reload/acme");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(WebJson);
        Assert.Contains("shutting down", problem.GetProperty("detail").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Reload of a known-but-inactive shell behaves like activate (Generation 1, no drain)")]
    public async Task Reload_ShellWasNeverActivated_StillReturnsSuccess()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("fresh", _ => { }));

        var response = await fixture.PostAsync("/admin/reload/fresh");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReloadResultResponse>(WebJson);
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.NewShell);
        Assert.Equal(1, body.NewShell.Generation);
        Assert.Null(body.Drain); // No previous generation to drain.
        Assert.Null(body.Error);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
}
