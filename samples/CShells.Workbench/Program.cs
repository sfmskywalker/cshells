using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Workbench.Background;
using CShells.Workbench.Features.Core;

var builder = WebApplication.CreateBuilder(args);

// Load shells from appsettings.json — three tenants with escalating feature tiers.
// Use From* members to select discovery sources explicitly; WithAssemblyProvider(...) is only needed when attaching a custom provider.
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.FromHostAssemblies(); // Re-include the built-in host-derived source explicitly for this sample.
    cshells.FromAssemblies(typeof(CoreFeature).Assembly); // Add the separate features assembly as an explicit source.
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
