# Contract: `IShellRegistry` (delta from feature 006)

**Feature**: [007-blueprint-provider-split](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/IShellRegistry.cs`

This document specifies the **delta** between feature `006`'s `IShellRegistry` and
feature `007`'s. Unchanged operations are listed for reference but not re-specified.

## Removed (from feature 006)

| Member | Reason |
|--------|--------|
| `void RegisterBlueprint(IShellBlueprint blueprint)` | Blueprints are owned by providers; not imperative registration. |
| `IShellBlueprint? GetBlueprint(string name)` | Blueprint access delegates to the provider via `GetBlueprintAsync`. |
| `IReadOnlyCollection<string> GetBlueprintNames()` | Catalogue names are paged via `ListAsync`, not bulk-returned. |
| `Task<IReadOnlyList<ReloadResult>> ReloadAllAsync(CancellationToken)` | Replaced by `ReloadActiveAsync(ReloadOptions)` with bounded parallelism. |

## Added

```csharp
/// <summary>
/// Returns the active generation for <paramref name="name"/> if present, else performs a
/// provider lookup, builds the next generation, runs initializers, promotes to Active,
/// and returns it. Concurrent calls for the same inactive name are serialized — exactly
/// one provider lookup and one shell build is performed; all callers observe the same
/// instance.
/// </summary>
/// <exception cref="ShellBlueprintNotFoundException">
/// The provider returned <c>null</c> for this name.
/// </exception>
/// <exception cref="ShellBlueprintUnavailableException">
/// The provider threw during lookup (e.g., backing store unreachable).
/// </exception>
Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default);

/// <summary>
/// Returns the blueprint registered for <paramref name="name"/> via the composite
/// provider, without activating a shell. Returns <c>null</c> when no provider claims
/// the name.
/// </summary>
/// <exception cref="ShellBlueprintUnavailableException">
/// The provider threw during lookup.
/// </exception>
Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default);

/// <summary>
/// Returns the manager associated with <paramref name="name"/>'s owning provider, or
/// <c>null</c> when the blueprint's source is read-only or the name is unknown.
/// </summary>
Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default);

/// <summary>
/// Removes the blueprint for <paramref name="name"/> in two ordered phases:
/// (1) persists the delete by invoking the owning manager's <c>DeleteAsync</c>;
/// (2) drains and disposes any active generation, then removes the in-memory slot.
/// </summary>
/// <exception cref="BlueprintNotMutableException">
/// The blueprint's source is read-only (no manager). No runtime state changes.
/// </exception>
/// <exception cref="ShellBlueprintNotFoundException">
/// The composite provider does not know this name.
/// </exception>
Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default);

/// <summary>
/// Paginated view of the catalogue left-joined with the registry's in-memory lifecycle
/// state. Blueprints with no active generation appear with null lifecycle fields.
/// </summary>
Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default);

/// <summary>
/// Reloads every currently-active shell in parallel, bounded by
/// <paramref name="options"/>.MaxDegreeOfParallelism. Per-shell outcomes are returned;
/// a failure for one shell does not abort the batch.
/// </summary>
Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default);
```

## Unchanged (from feature 006)

- `Task<IShell> ActivateAsync(string name, CancellationToken)` — still throws when a
  shell for this name is already active (use `GetOrActivateAsync` or `ReloadAsync`
  instead). Now consults the composite provider for blueprint lookup.
- `Task<ReloadResult> ReloadAsync(string name, CancellationToken)` — still serializes
  per-name. Now consults the composite provider for blueprint lookup.
- `Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken)` — unchanged.
- `IShell? GetActive(string name)` — unchanged in behavior; unchanged in semantics.
- `IReadOnlyCollection<IShell> GetAll(string name)` — unchanged; returns all generations
  (active + draining) for the name.
- `void Subscribe(IShellLifecycleSubscriber subscriber)` — unchanged.
- `void Unsubscribe(IShellLifecycleSubscriber subscriber)` — unchanged.

## Activation serialization

`GetOrActivateAsync`, `ActivateAsync`, `ReloadAsync`, and `UnregisterBlueprintAsync` all
acquire the same per-name `SemaphoreSlim(1,1)` stored on the `NameSlot` for the target
shell. This closes every concurrent-operation race:

- Stampede: 1 000 callers of `GetOrActivateAsync("X")` for an inactive name — one wins,
  activates, publishes; the rest see the active shell on their fast-path check after
  their turn at the semaphore.
- Reload-during-activation: `ReloadAsync("X")` queues behind the activation and
  executes afterwards against the freshly-active shell.
- Unregister-during-activation: `UnregisterBlueprintAsync("X")` queues behind the
  activation; once it runs, it drains the newly-active shell and clears the slot.

## Failure semantics

- **Provider throws during lookup**: wrapped in `ShellBlueprintUnavailableException` for
  `GetOrActivateAsync`. The `NameSlot.ActiveShell` is left null; a subsequent call
  retries the lookup. `GetBlueprintAsync` does NOT wrap — the exception propagates raw,
  leaving the choice of retry semantics to the caller.
- **Initializer throws during activation**: the partial provider is disposed via the
  existing feature-`006` `DisposePartialProviderAsync` helper; `ActiveShell` stays null;
  caller sees the initializer's exception. Mirrors `ActivateAsync` behavior unchanged.
- **Manager throws during `UnregisterBlueprintAsync`**: the exception propagates. The
  in-memory state is unchanged (no drain was initiated). The caller may retry after
  resolving the underlying cause.
