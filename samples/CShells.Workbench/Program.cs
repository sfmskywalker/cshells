using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Workbench.Background;
using CShells.Workbench.Features.Core;

var builder = WebApplication.CreateBuilder(args);

// Load shells from appsettings.json — three tenants with escalating feature tiers.
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.WithHostAssemblies(); // Re-include the built-in host-derived source explicitly for this sample.
    cshells.WithAssemblyContaining<CoreFeature>(); // Add the separate features assembly as an explicit source.

    // In 007, activation is lazy — shells come up on first touch rather than at startup. This
    // sample pre-warms the known tenants so the demo worker has shells to iterate from t=0 and
    // so end-to-end tests can assert post-startup state without triggering an HTTP request.
    cshells.PreWarmShells("Default", "Acme", "Contoso");
});

// Background service that logs a heartbeat for each active shell every 30 s.
// Demonstrates IShellRegistry + IShell.BeginScope() for background work.
builder.Services.AddHostedService<ShellDemoWorker>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();

// Host-level diagnostics via the new lifecycle registry. Pages the catalogue (which may be
// larger than the hot set), left-joining each entry with the registry's active-shell state.
app.MapGet("/_shells/status", async (IShellRegistry registry) =>
{
    var entries = new List<object>();
    string? cursor = null;
    do
    {
        var page = await registry.ListAsync(new ShellListQuery(Cursor: cursor, Limit: 100));
        foreach (var summary in page.Items)
        {
            entries.Add(new
            {
                name = summary.Name,
                source = summary.SourceId,
                mutable = summary.Mutable,
                active = summary.ActiveGeneration is int gen ? new
                {
                    generation = gen,
                    state = summary.State?.ToString(),
                    activeScopes = summary.ActiveScopeCount,
                } : null,
                generations = registry.GetAll(summary.Name)
                    .Select(s => new
                    {
                        generation = s.Descriptor.Generation,
                        state = s.State.ToString(),
                    })
                    .ToList(),
            });
        }
        cursor = page.NextCursor;
    } while (cursor is not null);

    return Results.Ok(entries);
});

app.MapShells();
app.Run();

// Make Program class accessible for WebApplicationFactory in end-to-end tests
namespace CShells.Workbench
{
    public partial class Program;
}
