# Contract: `IShellBlueprintManager`

**Feature**: [007-blueprint-provider-split](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/IShellBlueprintManager.cs`

Optional write-side peer of a blueprint provider. Implemented only by providers whose
underlying source supports mutation (database, blob store, etc.). Providers wrapping
read-only sources (configuration files, code-seeded blueprints) do NOT implement this
interface.

## Interface

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Write-side contract paired with an <see cref="IShellBlueprintProvider"/> whose source
/// accepts mutation. Persists create / update / delete operations; the registry independently
/// handles runtime cleanup (drain + dispose) after <c>DeleteAsync</c>.
/// </summary>
public interface IShellBlueprintManager
{
    /// <summary>
    /// Fast predicate: does this manager claim ownership of <paramref name="name"/>?
    /// Used when callers hold a manager reference directly; the registry discovers
    /// manager association via the owning provider's <see cref="ProvidedBlueprint"/>.
    /// </summary>
    bool Owns(string name);

    /// <summary>
    /// Persists a new blueprint derived from <paramref name="settings"/>. The settings'
    /// <c>Id.Name</c> is the key.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a blueprint for the given name already exists in the underlying store.
    /// </exception>
    Task CreateAsync(ShellSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the persisted blueprint for <paramref name="settings"/>.Id.Name. The
    /// running shell is NOT reloaded by this call; callers reload explicitly via
    /// <see cref="IShellRegistry.ReloadAsync"/> when ready.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no blueprint exists for the given name in the underlying store.
    /// </exception>
    Task UpdateAsync(ShellSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the persisted blueprint for <paramref name="name"/>. Does NOT touch the
    /// registry's runtime state — the registry drains any active generation itself as part
    /// of <see cref="IShellRegistry.UnregisterBlueprintAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no blueprint exists for the given name in the underlying store.
    /// </exception>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
```

## Behavioral requirements

- **Persistence-first**: every mutating method MUST NOT return until the underlying
  store has committed the change. Callers rely on this to sequence manager writes
  against `IShellRegistry.UnregisterBlueprintAsync`.
- **No runtime-state side effects**: `DeleteAsync` MUST NOT call into the registry or
  modify `IShell` state. The registry owns runtime cleanup via the existing drain
  machinery.
- **`Owns(name)` is cheap**: sub-millisecond, no I/O. Implementations typically use a
  prefix match or an in-memory cached set of known names.
- **Uniqueness check**: `CreateAsync` and `UpdateAsync` MUST enforce unique names within
  the manager's own source. Cross-source uniqueness is enforced by the composite
  provider's duplicate detection.
- **Concurrent safety**: implementations MUST be safe under concurrent mutation (e.g.,
  two operators creating different names simultaneously). Serialization MAY be coarse
  (process-local `SemaphoreSlim`) or fine (per-name store-level lock), at the
  implementer's discretion.

## Registry-mediated usage

The registry does not call `Owns`; it discovers the owning manager from the blueprint's
`ProvidedBlueprint.Manager`. `Owns` exists for direct callers (e.g., future admin API)
that hold a manager reference and need to route writes without a provider lookup.

## Reference implementations (in this feature)

| Type | Backing store |
|------|---------------|
| `FluentStorageShellBlueprintProvider` (also `IShellBlueprintManager`) | Blob container via FluentStorage |

Future providers (SQL, DynamoDB, etc.) implement the same contract.
