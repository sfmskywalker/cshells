using CShells.Lifecycle;

namespace CShells.Workbench.Background;

/// <summary>
/// A background service that demonstrates how to use <see cref="IShellRegistry"/> and
/// <see cref="IShell.BeginScope"/> to execute work within each shell's isolated service
/// provider. Each iteration iterates the currently-active shells, opens a scope per shell,
/// and logs a heartbeat with the shell's configuration.
/// </summary>
public class ShellDemoWorker(
    IShellRegistry registry,
    ILogger<ShellDemoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var name in registry.GetBlueprintNames())
            {
                var shell = registry.GetActive(name);
                if (shell is null)
                    continue;

                try
                {
                    await ExecuteForShellAsync(shell);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during background heartbeat for shell {Shell}", shell.Descriptor);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ExecuteForShellAsync(IShell shell)
    {
        await using var scope = shell.BeginScope();

        var config = scope.ServiceProvider.GetService<IConfiguration>();
        var plan = config?["Plan"] ?? "Unknown";
        var settings = scope.ServiceProvider.GetService<ShellSettings>();

        logger.LogInformation(
            "Heartbeat for shell {Shell} (Plan: {Plan}, Features: {Features})",
            shell.Descriptor,
            plan,
            string.Join(", ", settings?.EnabledFeatures ?? []));
    }
}
