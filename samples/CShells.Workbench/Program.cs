using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Management.Api;
using CShells.Workbench.Background;
using CShells.Workbench.Features.Core;

var builder = WebApplication.CreateBuilder(args);

// Load shells from appsettings.json — three tenants with escalating feature tiers.
// Activation is lazy — shells come up on first touch rather than at startup.
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
