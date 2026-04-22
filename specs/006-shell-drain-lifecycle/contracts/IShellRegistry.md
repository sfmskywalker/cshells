# Contract: IShellRegistry

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IShellRegistry` is the primary API for creating, promoting, draining, and replacing named,
versioned shells. It is the authoritative source of truth for all shell lifecycle state.

## Interface Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// The authoritative registry for named, versioned shells with explicit lifecycle management.
/// </summary>
/// <remarks>
/// Shells are created in <see cref="ShellLifecycleState.Initializing"/> state and must be
/// promoted to <see cref="ShellLifecycleState.Active"/> before they can serve requests.
/// Only one shell per name may be <see cref="ShellLifecycleState.Active"/> at a time.
/// Draining a shell allows in-flight work to complete before the service provider is disposed.
/// </remarks>
public interface IShellRegistry
{
    /// <summary>
    /// Creates a new shell and registers it in <see cref="ShellLifecycleState.Initializing"/> state.
    /// </summary>
    /// <param name="name">The shell name. Multiple shells can share a name; only one may be active.</param>
    /// <param name="version">The shell version. Combined with <paramref name="name"/> forms the unique identity.</param>
    /// <param name="configure">Delegate invoked to register services into the shell's <see cref="IServiceCollection"/>.</param>
    /// <param name="metadata">Optional opaque metadata carried on the shell descriptor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created <see cref="IShell"/> in <see cref="ShellLifecycleState.Initializing"/> state.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> or <paramref name="version"/> is null or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a shell with the same <c>(name, version)</c> identity already exists in the registry.
    /// </exception>
    /// <remarks>
    /// If <paramref name="configure"/> throws, the exception propagates to the caller and no shell
    /// entry is added to the registry.
    /// </remarks>
    Task<IShell> CreateAsync(
        string name,
        string version,
        Action<IServiceCollection> configure,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the single <see cref="ShellLifecycleState.Active"/> shell for the given name,
    /// or <c>null</c> if no active shell exists.
    /// </summary>
    IShell? GetActive(string name);

    /// <summary>
    /// Returns all shells registered under <paramref name="name"/>, regardless of lifecycle state.
    /// </summary>
    IReadOnlyCollection<IShell> GetAll(string name);

    /// <summary>
    /// Promotes <paramref name="shell"/> to <see cref="ShellLifecycleState.Active"/>,
    /// transitioning any previously active shell for the same name to
    /// <see cref="ShellLifecycleState.Deactivating"/>.
    /// </summary>
    /// <param name="shell">The shell to promote. Must be in <see cref="ShellLifecycleState.Initializing"/> state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="shell"/> is not in <see cref="ShellLifecycleState.Initializing"/> state.
    /// </exception>
    /// <remarks>
    /// Concurrent <c>PromoteAsync</c> calls for the same shell name are serialized; both succeed in
    /// arrival order and the last one to complete becomes the active shell.
    /// </remarks>
    Task PromoteAsync(IShell shell, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a drain on <paramref name="shell"/>, invoking all registered
    /// <see cref="IDrainHandler"/> instances in parallel.
    /// </summary>
    /// <param name="shell">The shell to drain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <see cref="IDrainOperation"/> handle. Concurrent calls for the same shell return
    /// the same in-flight operation.
    /// </returns>
    Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes <paramref name="newShell"/> to active and initiates drain on the shell it replaces,
    /// in a single atomic operation.
    /// </summary>
    /// <param name="newShell">The shell to promote. Must be in <see cref="ShellLifecycleState.Initializing"/> state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <see cref="IDrainOperation"/> for the shell that was displaced (the previous active shell),
    /// or <c>null</c> if there was no previously active shell.
    /// </returns>
    Task<IDrainOperation?> ReplaceAsync(IShell newShell, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a lifecycle subscriber that will be notified of all shell state transitions.
    /// </summary>
    void Subscribe(IShellLifecycleSubscriber subscriber);

    /// <summary>
    /// Removes a previously registered lifecycle subscriber.
    /// </summary>
    void Unsubscribe(IShellLifecycleSubscriber subscriber);
}
```

## Behaviour Contract

### CreateAsync

- Builds the shell's `IServiceProvider` by calling `configure` on a new `ServiceCollection`.
  Root services (logging, configuration) are copied in first; shell services registered in
  `configure` override them via "last-wins" semantics.
- The shell is registered atomically; if `configure` throws the registry is unchanged.
- Fires `OnStateChangedAsync(shell, null → Initializing)` on all subscribers after registration.

### PromoteAsync

- Serialized per shell name via `SemaphoreSlim(1,1)`.
- Atomically sets the promoted shell to `Active` and any existing `Active` shell to `Deactivating`.
- Fires `Active` transition event for the promoted shell.
- Fires `Deactivating` transition event for the displaced shell (if any).
- The displaced shell automatically progresses to `Draining` after `Deactivating`.

### DrainAsync

- Returns an existing `IDrainOperation` if drain is already in progress (idempotent).
- Transitions shell from `Deactivating` or `Active` → `Draining`.
- Resolves all `IDrainHandler` registrations from the shell's `IServiceProvider`.
- Invokes all handlers in parallel, each receiving a `CancellationToken` cancelled at deadline.
- Transitions to `Drained` when all handlers complete (or time out / are forced).

### ReplaceAsync

- Equivalent to `PromoteAsync(newShell)` followed by `DrainAsync(displaced)`.
- Both operations share the same promote serialization lock.
