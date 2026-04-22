using CShells.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Hosting;

/// <summary>
/// Activates every registered blueprint at host start and drains every active shell at host
/// stop (bounded by the host's shutdown timeout; providers are disposed on breach so the host
/// actually exits — FR-036).
/// </summary>
internal sealed class CShellsStartupHostedService(
    IShellRegistry registry,
    ILogger<CShellsStartupHostedService>? logger = null) : IHostedService
{
    private readonly IShellRegistry _registry = Guard.Against.Null(registry);
    private readonly ILogger<CShellsStartupHostedService> _logger = logger ?? NullLogger<CShellsStartupHostedService>.Instance;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var names = _registry.GetBlueprintNames();
        if (names.Count == 0)
        {
            _logger.LogInformation("CShells startup: no blueprints registered; registry idle.");
            return;
        }

        _logger.LogInformation("CShells startup: activating {Count} blueprint(s).", names.Count);

        // Activate in parallel. Skip any blueprint that is already active (e.g., a host that
        // called ActivateAsync explicitly after RegisterBlueprint).
        var tasks = names.Select(async name =>
        {
            if (_registry.GetActive(name) is not null)
                return;

            try
            {
                await _registry.ActivateAsync(name, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already has an Active", StringComparison.Ordinal))
            {
                // Benign race with an explicit ActivateAsync call from host code.
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var actives = _registry.GetBlueprintNames()
            .Select(n => _registry.GetActive(n))
            .OfType<IShell>()
            .ToList();

        if (actives.Count == 0)
            return;

        _logger.LogInformation("CShells shutdown: draining {Count} active shell(s).", actives.Count);

        var drains = await Task.WhenAll(actives.Select(s => _registry.DrainAsync(s, cancellationToken))).ConfigureAwait(false);

        // Wait for every drain to complete OR the shutdown token to fire. Drain operations
        // already dispose their shell at the end of the normal path; shutdown-timeout breach
        // forces emergency disposal via the registry.
        var waitTasks = drains.Select(d => d.WaitAsync(cancellationToken));
        try
        {
            await Task.WhenAll(waitTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "CShells shutdown exceeded the host's shutdown timeout; emergency-disposing shells whose drain did not complete.");

            // Emergency dispose anything still not Disposed.
            foreach (var shell in actives.OfType<Lifecycle.Shell>())
            {
                if (shell.State != Lifecycle.ShellLifecycleState.Disposed)
                    await shell.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
