using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Management;
using CShells.Workbench.Background;
using CShells.Workbench.Features.Core;

var builder = WebApplication.CreateBuilder(args);

// Load shells from appsettings.json — three tenants with escalating feature tiers.
// Use From* members to select discovery sources explicitly; WithAssemblyProvider(...) is only needed when attaching a custom provider.
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.WithHostAssemblies(); // Re-include the built-in host-derived source explicitly for this sample.
    cshells.WithAssemblyContaining<CoreFeature>(); // Add the separate features assembly as an explicit source.
});

// Background service that logs a heartbeat for each active shell every 30 s.
// Demonstrates IShellHost + IShellContextScopeFactory for background work.
builder.Services.AddHostedService<ShellDemoWorker>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();

// Host-level diagnostics stay available even when a shell's newest desired state is deferred.
// Shell-owned routes are still exposed only for committed applied runtimes via MapShells().
app.MapGet("/_shells/status", (IShellRuntimeStateAccessor runtimeState) =>
    Results.Ok(runtimeState.GetAllShells().Select(status => new
    {
        shellId = status.ShellId.Name,
        status.DesiredGeneration,
        status.AppliedGeneration,
        outcome = status.Outcome.ToString(),
        status.IsInSync,
        status.IsRoutable,
        status.BlockingReason,
        missingFeatures = status.MissingFeatures
    })));

app.MapShells();
app.Run();

// Make Program class accessible for WebApplicationFactory in end-to-end tests
namespace CShells.Workbench
{
    public partial class Program;
}
