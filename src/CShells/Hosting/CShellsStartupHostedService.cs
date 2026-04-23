using CShells.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Hosting;

/// <summary>
/// Host startup coordinator:
/// (1) resolves every registered <see cref="IShellBlueprintProvider"/> and registers the
/// blueprints they return;
/// (2) activates every registered blueprint in parallel (FR-035);
/// (3) on stop, drains every active shell using the configured drain policy and disposes them,
/// emergency-disposing any that don't complete within the host's shutdown timeout (FR-036).
/// </summary>
internal sealed class CShellsStartupHostedService(
    IShellRegistry registry,
    IEnumerable<IShellBlueprintProvider> blueprintProviders,
    ILogger<CShellsStartupHostedService>? logger = null) : IHostedService
{
    private readonly IShellRegistry _registry = Guard.Against.Null(registry);
    private readonly IEnumerable<IShellBlueprintProvider> _blueprintProviders = Guard.Against.Null(blueprintProviders);
    private readonly ILogger<CShellsStartupHostedService> _logger = logger ?? NullLogger<CShellsStartupHostedService>.Instance;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RegisterAsyncBlueprintsAsync(cancellationToken).ConfigureAwait(false);

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
            catch (InvalidOperationException)
            {
                // Benign race with an explicit ActivateAsync call from host code: another
                // caller activated the shell between our pre-check and this call. Verify via
                // the registry's state (not the exception message — message text is not a
                // stable API) and rethrow if the state isn't what the race would produce.
                if (_registry.GetActive(name) is null)
                    throw;
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RegisterAsyncBlueprintsAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in _blueprintProviders)
        {
            IReadOnlyList<IShellBlueprint> blueprints;
            try
            {
                blueprints = await provider.GetBlueprintsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Shell blueprint provider {ProviderType} threw during startup; no blueprints contributed from it.",
                    provider.GetType().FullName);
                throw;
            }

            foreach (var blueprint in blueprints)
            {
                // Ignore duplicates: a blueprint already registered by fluent AddShell or a
                // prior provider wins — providers never overwrite existing registrations.
                if (_registry.GetBlueprint(blueprint.Name) is not null)
                {
                    _logger.LogDebug(
                        "Blueprint provider {ProviderType} returned blueprint '{Name}' but one is already registered; skipping.",
                        provider.GetType().FullName, blueprint.Name);
                    continue;
                }

                _registry.RegisterBlueprint(blueprint);
            }
        }
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
