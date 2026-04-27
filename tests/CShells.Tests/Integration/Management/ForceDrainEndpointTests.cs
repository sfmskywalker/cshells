using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Management.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Management;

/// <summary>
/// Integration tests for <c>POST /{name}/force-drain</c> (US4). Covers single in-flight
/// drain, two simultaneously-draining generations, the no-in-flight 404, the unknown-name
/// 404, and the only-Drained edge case.
/// </summary>
public class ForceDrainEndpointTests
{
    [Fact(DisplayName = "Force-drain with one in-flight generation returns 200 with a single result")]
    public async Task ForceDrain_OneInflightGeneration_Returns200_WithSingleResult_StatusForcedOrCompleted()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .WithAssemblyContaining<ForceDrainEndpointTests>()
            .AddShell("acme", s => s.WithFeature<MgmtStuckDrainFeature>()));
        await fixture.Registry.GetOrActivateAsync("acme");

        // Reload once to leave one generation draining.
        await fixture.PostAsync("/admin/reload/acme");

        var response = await fixture.PostAsync("/admin/acme/force-drain");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DrainResultResponse[]>(WebJson);
        Assert.NotNull(body);
        Assert.Single(body);
        Assert.Contains(body[0].Status, new[] { "Forced", "Completed" });
    }

    [Fact(DisplayName = "Force-drain with two in-flight generations returns 200 with two results")]
    public async Task ForceDrain_TwoInflightGenerations_Returns200_WithTwoResults()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .WithAssemblyContaining<ForceDrainEndpointTests>()
            .AddShell("acme", s => s.WithFeature<MgmtStuckDrainFeature>()));
        await fixture.Registry.GetOrActivateAsync("acme");

        // Two consecutive reloads → gen 1 and gen 2 both draining; gen 3 is active.
        await fixture.PostAsync("/admin/reload/acme");
        await fixture.PostAsync("/admin/reload/acme");

        var response = await fixture.PostAsync("/admin/acme/force-drain");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DrainResultResponse[]>(WebJson);
        Assert.NotNull(body);
        Assert.Equal(2, body.Length);
        Assert.All(body, entry => Assert.Contains(entry.Status, new[] { "Forced", "Completed" }));
    }

    [Fact(DisplayName = "Force-drain with no in-flight drain returns 404")]
    public async Task ForceDrain_NoInflightDrain_Returns404()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("acme", _ => { }));
        await fixture.Registry.GetOrActivateAsync("acme");

        var response = await fixture.PostAsync("/admin/acme/force-drain");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(WebJson);
        Assert.Contains("no in-flight drain", problem.GetProperty("detail").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Force-drain of an unknown shell name returns 404")]
    public async Task ForceDrain_UnknownName_Returns404()
    {
        await using var fixture = new ManagementApiFixture(c => c.AddShell("known", _ => { }));

        var response = await fixture.PostAsync("/admin/does-not-exist/force-drain");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "Force-drain when only Drained generations remain returns 404")]
    public async Task ForceDrain_OnlyDrainedGenerations_Returns404()
    {
        await using var fixture = new ManagementApiFixture(c => c
            .AddShell("acme", _ => { })); // No drain handler — drain completes immediately.
        await fixture.Registry.GetOrActivateAsync("acme");

        // Reload completes drain immediately; old gen advances all the way to Disposed/Drained.
        await fixture.PostAsync("/admin/reload/acme");

        // Wait briefly for drain to finish.
        await Task.Delay(100);

        var response = await fixture.PostAsync("/admin/acme/force-drain");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public sealed class MgmtStuckDrainFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddTransient<IDrainHandler, MgmtStuckDrainHandler>();
    }

    private sealed class MgmtStuckDrainHandler : IDrainHandler
    {
        public async Task DrainAsync(IDrainExtensionHandle _, CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }
}
