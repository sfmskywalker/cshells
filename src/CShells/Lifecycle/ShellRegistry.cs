using System.Collections.Concurrent;
using System.Collections.Immutable;
using CShells.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Lifecycle;

/// <summary>
/// Default <see cref="IShellRegistry"/> implementation.
/// </summary>
/// <remarks>
/// Phase 3 (US8) scope: blueprint storage, subscriber fan-out, and the
/// <see cref="FireStateChangedAsync"/> helper that every <see cref="Shell"/> invokes on
/// transition. The activate/reload/drain methods are filled in by subsequent phases.
/// </remarks>
internal sealed class ShellRegistry(ILogger<ShellRegistry>? logger = null) : IShellRegistry
{
    private readonly ILogger<ShellRegistry> _logger = logger ?? NullLogger<ShellRegistry>.Instance;
    private readonly ConcurrentDictionary<string, IShellBlueprint> _blueprints = new(StringComparer.OrdinalIgnoreCase);
    private ImmutableList<IShellLifecycleSubscriber> _subscribers = [];

    /// <inheritdoc />
    public void RegisterBlueprint(IShellBlueprint blueprint)
    {
        Guard.Against.Null(blueprint);
        Guard.Against.NullOrWhiteSpace(blueprint.Name, nameof(blueprint) + "." + nameof(blueprint.Name));

        if (!_blueprints.TryAdd(blueprint.Name, blueprint))
            throw new InvalidOperationException(
                $"A blueprint is already registered for shell '{blueprint.Name}'. Duplicate registration is a programming error.");
    }

    /// <inheritdoc />
    public IShellBlueprint? GetBlueprint(string name)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return _blueprints.TryGetValue(name, out var blueprint) ? blueprint : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetBlueprintNames() => [.._blueprints.Keys];

    /// <inheritdoc />
    public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ActivateAsync is filled in by Phase 4 (US1).");

    /// <inheritdoc />
    public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ReloadAsync is filled in by Phase 8 (US2).");

    /// <inheritdoc />
    public Task<IReadOnlyList<ReloadResult>> ReloadAllAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ReloadAllAsync is filled in by Phase 9 (US3).");

    /// <inheritdoc />
    public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("DrainAsync is filled in by Phase 6 (US5).");

    /// <inheritdoc />
    public IShell? GetActive(string name)
        => throw new NotImplementedException("GetActive is filled in by Phase 4 (US1).");

    /// <inheritdoc />
    public IReadOnlyCollection<IShell> GetAll(string name)
        => throw new NotImplementedException("GetAll is filled in by Phase 4 (US1).");

    /// <inheritdoc />
    public void Subscribe(IShellLifecycleSubscriber subscriber)
    {
        Guard.Against.Null(subscriber);
        ImmutableInterlocked.Update(ref _subscribers,
            static (list, s) => list.Contains(s) ? list : list.Add(s),
            subscriber);
    }

    /// <inheritdoc />
    public void Unsubscribe(IShellLifecycleSubscriber subscriber)
    {
        Guard.Against.Null(subscriber);
        ImmutableInterlocked.Update(ref _subscribers,
            static (list, s) => list.Remove(s),
            subscriber);
    }

    /// <summary>
    /// Fans out a state-change event to every registered subscriber. Subscriber exceptions
    /// are caught and logged so one failing subscriber cannot block others or the transition.
    /// </summary>
    internal async Task FireStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _subscribers;
        if (snapshot.IsEmpty)
            return;

        foreach (var subscriber in snapshot)
        {
            try
            {
                await subscriber.OnStateChangedAsync(shell, previous, current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Shell lifecycle subscriber {SubscriberType} threw during {Previous} → {Current} for shell {Shell}",
                    subscriber.GetType().FullName, previous, current, shell.Descriptor);
            }
        }
    }
}
