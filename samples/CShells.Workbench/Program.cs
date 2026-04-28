using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Management.Api;
using CShells.Workbench.Background;
using CShells.Workbench.Features.Core;

var builder = WebApplication.CreateBuilder(args);

// Load shells from appsettings.json — three tenants with escalating feature tiers.
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.WithHostAssemblies(); // Re-include the built-in host-derived source explicitly for this sample.
    cshells.WithAssemblyContaining<CoreFeature>(); // Add the separate features assembly as an explicit source.

    // Routing activates shells lazily on first request (feature 010). This sample still
    // pre-warms so the demo background worker has shells to iterate from t=0 and so
    // end-to-end tests can assert post-startup state without first issuing an HTTP request.
    // PreWarmShells is a perf hint now, not a correctness requirement: removing this line
    // still serves requests correctly — the first request to each shell pays activation cost.
    cshells.PreWarmShells("Default", "Acme", "Contoso");
});

// Background service that logs a heartbeat for each active shell every 30 s.
// Demonstrates IShellRegistry + IShell.BeginScope() for background work.
builder.Services.AddHostedService<ShellDemoWorker>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();

app.MapShells();

// Sample-only: management endpoints are unprotected. In production, chain
// .RequireAuthorization(...) on the returned RouteGroupBuilder. The endpoints expose direct
// control over the shell registry (reload, force-drain) and return registered
// ConfigurationData verbatim, so an unprotected install is a foot-gun outside dev environments.
app.MapShellManagementApi("/_admin/shells");

app.Run();

// Make Program class accessible for WebApplicationFactory in end-to-end tests
namespace CShells.Workbench
{
    public partial class Program;
}
