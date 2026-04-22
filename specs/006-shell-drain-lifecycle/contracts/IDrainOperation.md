# Contract: IDrainOperation

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IDrainOperation` is the observable handle returned by `IShellRegistry.DrainAsync` and
surfaced on `ReloadResult.Drain`. It exposes the current drain status, the deadline, a
completion awaitable, and a force method. The operation coordinates drain's three phases
internally: scope wait, handler invocation, grace.

## Interface Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Represents an in-progress or completed drain operation on a shell.
/// </summary>
/// <remarks>
/// Obtain an instance via <see cref="IShellRegistry.DrainAsync"/> or via
/// <see cref="ReloadResult.Drain"/>. Concurrent callers for the same shell receive the same
/// instance.
/// </remarks>
public interface IDrainOperation
{
    /// <summary>
    /// Gets the current status of the drain operation.
    /// </summary>
    DrainStatus Status { get; }

    /// <summary>
    /// Gets the UTC deadline by which all handlers must complete, or <c>null</c> for an
    /// unbounded policy.
    /// </summary>
    DateTimeOffset? Deadline { get; }

    /// <summary>
    /// Awaits drain completion and returns the structured result.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the await, but does NOT cancel the drain itself. The drain continues in the
    /// background regardless.
    /// </param>
    /// <returns>
    /// A <see cref="DrainResult"/> containing per-handler outcomes and overall status.
    /// </returns>
    Task<DrainResult> WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all outstanding handler tokens and transitions the shell to
    /// <see cref="ShellLifecycleState.Drained"/> after the configured grace period.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the force operation itself.</param>
    /// <remarks>
    /// After this call returns, <see cref="Status"/> is <see cref="DrainStatus.Forced"/> and
    /// <see cref="WaitAsync"/> resolves with a result whose status is
    /// <see cref="DrainStatus.Forced"/>. Forcing during the scope-wait phase skips the
    /// remainder of that phase as well.
    /// </remarks>
    Task ForceAsync(CancellationToken cancellationToken = default);
}
```

## Supporting Types

```csharp
namespace CShells.Lifecycle;

/// <summary>Overall outcome of a drain operation.</summary>
public enum DrainStatus
{
    /// <summary>Drain is in progress.</summary>
    Pending,

    /// <summary>All handlers completed within the deadline.</summary>
    Completed,

    /// <summary>The deadline elapsed; handlers were cancelled.</summary>
    TimedOut,

    /// <summary><see cref="IDrainOperation.ForceAsync"/> was called.</summary>
    Forced,
}

/// <summary>Structured result returned by <see cref="IDrainOperation.WaitAsync"/>.</summary>
public sealed record DrainResult(
    ShellDescriptor Shell,
    DrainStatus Status,
    TimeSpan ScopeWaitElapsed,
    int AbandonedScopeCount,
    IReadOnlyList<DrainHandlerResult> HandlerResults);

/// <summary>Outcome for a single drain handler.</summary>
public sealed record DrainHandlerResult(
    string HandlerTypeName,
    bool Completed,
    TimeSpan Elapsed,
    Exception? Error);
```

## Drain Phases

Every drain operation runs these phases in order. The overall drain deadline is shared
across phases 1 and 2; the grace period (phase 3) is separate.

1. **Scope wait**. Await the shell's active-scope counter reaching zero. Bounded by the
   drain deadline. During this phase no `IDrainHandler` runs. Outstanding scopes at the
   deadline are abandoned (not forcibly disposed) and phase 2 proceeds with the cancelled
   token. `DrainResult.ScopeWaitElapsed` records the duration of this phase;
   `DrainResult.AbandonedScopeCount` records how many handles were still outstanding when
   the phase ended (zero in the normal case).
2. **Handler invocation**. Resolve `IEnumerable<IDrainHandler>` from the shell's provider
   and invoke all handlers in parallel with a cancellation token linked to the remaining
   deadline budget. Handlers may request extensions via the `IDrainExtensionHandle` the
   policy grants.
3. **Grace**. After the deadline elapses (phase 1 or 2) or `ForceAsync` is called, wait up
   to the grace period for still-running handlers to observe cancellation. Transition to
   `Drained` regardless of remaining handler state.

## Behaviour Contract

### WaitAsync

- Returns immediately if drain has already completed.
- Cancelling `cancellationToken` abandons the await; the drain operation continues in the
  background.
- Multiple callers may `WaitAsync` concurrently; all receive the same `DrainResult`.

### ForceAsync

- If the drain is already completed (`Status != Pending`), this is a no-op.
- Cancels the active-scope-wait and all handler `CancellationTokenSource` instances
  immediately.
- Waits up to the configured grace period for handlers to acknowledge cancellation.
- Sets `Status` to `Forced` and completes the drain.

### Concurrent calls

- `DrainAsync` on the same shell while a drain is in progress returns the existing
  `IDrainOperation`. No second drain is ever started (SC-006).
- `ForceAsync` may be called by any holder of the operation handle.
