using CShells.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Hosting;

/// <summary>
/// Host startup coordinator:
/// (1) activates any pre-warm names configured via <c>CShellsBuilder.PreWarmShells(...)</c>;
/// (2) on stop, drains every active shell using the configured drain policy and disposes them,
/// emergency-disposing any that don't complete within the host's shutdown timeout.
/// </summary>
/// <remarks>
/// After feature <c>007</c>, startup does NOT enumerate the catalogue. Host startup time is
/// O(pre-warm set) — typically zero. Inactive blueprints are activated lazily on first touch
/// (via <see cref="IShellRegistry.GetOrActivateAsync"/>, typically from routing middleware).
/// </remarks>
internal sealed class CShellsStartupHostedService(
    IShellRegistry registry,
    PreWarmShellList preWarmList,
    ILogger<CShellsStartupHostedService>? logger = null) : IHostedService
{
    private readonly IShellRegistry _registry = Guard.Against.Null(registry);
    private readonly PreWarmShellList _preWarmList = Guard.Against.Null(preWarmList);
    private readonly ILogger<CShellsStartupHostedService> _logger = logger ?? NullLogger<CShellsStartupHostedService>.Instance;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_preWarmList.Count == 0)
        {
            _logger.LogInformation("CShells startup: no shells pre-warmed; routing will activate shells lazily on first request.");
            return;
        }

        _logger.LogInformation("CShells startup: pre-warming {Count} shell(s).", _preWarmList.Count);

        // Pre-warm in parallel. A single failure is logged and does not abort startup — the host
        // may still serve requests for other shells. Callers who need strict pre-warming should
        // pre-warm from their own IHostedService with their own error policy.
        var tasks = _preWarmList.Select(async name =>
        {
            try
            {
                await _registry.GetOrActivateAsync(name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pre-warm activation failed for shell '{Name}'; host will continue.", name);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The registry's in-memory index is the authoritative source of active shells at
        // shutdown. GetActiveShells reads only the in-memory slot dict — zero I/O — so
        // shutdown is decoupled from provider/storage availability.
        var actives = _registry.GetActiveShells().ToList();

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

/// <summary>
/// Singleton container for the list of shell names to pre-warm at startup. Populated by
/// <c>CShellsBuilder.PreWarmShells(...)</c>; consumed by <see cref="CShellsStartupHostedService"/>.
/// </summary>
internal sealed class PreWarmShellList : List<string>
{
    public PreWarmShellList() { }
    public PreWarmShellList(IEnumerable<string> names) : base(names) { }
}
