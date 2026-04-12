using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Workbench.Background;
using CShells.Workbench.Features.Core;

var builder = WebApplication.CreateBuilder(args);

// Load shells from appsettings.json — three tenants with escalating feature tiers.
// Configure feature discovery fluently so the features assembly is included explicitly.
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.FromHostAssemblies(); // Host assembly is included by default, but we call this to demonstrate fluent configuration.
    cshells.FromAssemblies(typeof(CoreFeature).Assembly);
});

// Background service that logs a heartbeat for each active shell every 30 s.
// Demonstrates IShellHost + IShellContextScopeFactory for background work.
builder.Services.AddHostedService<ShellDemoWorker>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();
app.MapShells();
app.Run();

// Make Program class accessible for WebApplicationFactory in end-to-end tests
namespace CShells.Workbench
{
    public partial class Program;
}