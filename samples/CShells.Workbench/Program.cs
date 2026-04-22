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
});

// Background service that logs a heartbeat for each active shell every 30 s.
// Demonstrates IShellRegistry + IShell.BeginScope() for background work.
builder.Services.AddHostedService<ShellDemoWorker>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();

// Host-level diagnostics via the new lifecycle registry.
app.MapGet("/_shells/status", (IShellRegistry registry) =>
    Results.Ok(registry.GetBlueprintNames()
        .Select(name => new
        {
            name,
            active = registry.GetActive(name) is { } active ? new
            {
                generation = active.Descriptor.Generation,
                state = active.State.ToString(),
                createdAt = active.Descriptor.CreatedAt,
            } : null,
            generations = registry.GetAll(name)
                .Select(s => new
                {
                    generation = s.Descriptor.Generation,
                    state = s.State.ToString(),
                })
                .ToList(),
        })));

app.MapShells();
app.Run();

// Make Program class accessible for WebApplicationFactory in end-to-end tests
namespace CShells.Workbench
{
    public partial class Program;
}
