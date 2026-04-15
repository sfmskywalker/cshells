using CShells.Management;
using CShells.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Hosting;

/// <summary>
/// Ensures shells are reconciled into applied runtimes during application startup and deactivated during shutdown.
/// </summary>
public class ShellStartupHostedService : IHostedService
{
    private readonly IShellHost shellHost;
    private readonly DefaultShellManager shellManager;
    private readonly IShellRuntimeStateAccessor runtimeStateAccessor;
    private readonly INotificationPublisher notificationPublisher;
    private readonly ILogger<ShellStartupHostedService> logger;
    private readonly object lifecycleLock = new();
    private Task? startTask;
    private Task? stopTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellStartupHostedService"/> class.
    /// </summary>
    public ShellStartupHostedService(
        IShellHost shellHost,
        DefaultShellManager shellManager,
        IShellRuntimeStateAccessor runtimeStateAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<ShellStartupHostedService>? logger = null)
    {
        this.shellHost = Guard.Against.Null(shellHost);
        this.shellManager = Guard.Against.Null(shellManager);
        this.runtimeStateAccessor = Guard.Against.Null(runtimeStateAccessor);
        this.notificationPublisher = Guard.Against.Null(notificationPublisher);
        this.logger = logger ?? NullLogger<ShellStartupHostedService>.Instance;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycleLock)
        {
            startTask ??= StartCoreAsync(cancellationToken);
            return startTask;
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconciling shells on application startup");

        await shellManager.InitializeRuntimeAsync(cancellationToken).ConfigureAwait(false);
        var statuses = runtimeStateAccessor.GetAllShells();

        await notificationPublisher.PublishAsync(new ShellsReloaded(statuses), strategy: null, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Startup reconciliation completed for {ShellCount} configured shell(s); {ActiveCount} shell(s) are currently applied",
            statuses.Count,
            statuses.Count(status => status.IsRoutable));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycleLock)
        {
            stopTask ??= StopCoreAsync(cancellationToken);
            return stopTask;
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Deactivating applied shells on application shutdown");

        IReadOnlyCollection<ShellContext> shells;
        try
        {
            shells = shellHost.AllShells;
        }
        catch (ObjectDisposedException)
        {
            logger.LogDebug("Shell host already disposed, skipping deactivation");
            return;
        }

        var failedCount = 0;

        foreach (var shell in shells)
        {
            try
            {
                await notificationPublisher.PublishAsync(new ShellDeactivating(shell), strategy: null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failedCount++;
                logger.LogError(ex, "Failed to deactivate shell '{ShellId}' during application shutdown", shell.Id);
            }
        }

        if (failedCount > 0)
        {
            logger.LogWarning(
                "Deactivated {SuccessCount}/{TotalCount} shell(s) ({FailedCount} failed)",
                shells.Count - failedCount,
                shells.Count,
                failedCount);
        }
        else
        {
            logger.LogInformation("Successfully deactivated {ShellCount} shell(s)", shells.Count);
        }
    }
}
