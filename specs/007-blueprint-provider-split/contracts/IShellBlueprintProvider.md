# Contract: `IShellBlueprintProvider`

**Feature**: [007-blueprint-provider-split](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/IShellBlueprintProvider.cs`

Source-agnostic blueprint catalogue. Lazy lookup + paginated listing. This interface
replaces the eager contract introduced in feature `006`.

## Interface

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Source of shell blueprints. Supports on-demand lookup and paginated listing; does not
/// require eager enumeration.
/// </summary>
/// <remarks>
/// Multiple providers can be registered in DI; the built-in composite multiplexes them.
/// Each provider SHOULD expose a stable <c>SourceId</c> via the <see cref="BlueprintSummary"/>
/// entries it returns from <see cref="ListAsync"/>.
/// </remarks>
public interface IShellBlueprintProvider
{
    /// <summary>
    /// Returns the blueprint for <paramref name="name"/>, paired with its owning manager
    /// if the underlying source supports mutation. Returns <c>null</c> when the provider
    /// does not claim this name.
    /// </summary>
    /// <exception cref="Exception">
    /// Implementations MAY throw on I/O faults; the registry wraps these in
    /// <see cref="ShellBlueprintUnavailableException"/> before surfacing to callers.
    /// </exception>
    Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cheap existence check. Default: <c>GetAsync(name) is not null</c>. Implementations
    /// MAY override for a faster path (e.g., a blob-presence check that doesn't download).
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
        => GetAsync(name, cancellationToken).ContinueWith(
            t => t.Result is not null,
            cancellationToken,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);

    /// <summary>
    /// Paginated listing of blueprints this provider contributes. The cursor is opaque to
    /// callers and defined by each implementation.
    /// </summary>
    Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default);
}
```

## Behavioral requirements

- **No eager enumeration**: `GetAsync` and `ExistsAsync` MUST be O(1) with respect to
  catalogue size, or O(log N) at worst for tree-indexed stores. They MUST NOT iterate
  the full catalogue.
- **Null result is valid**: `GetAsync` returning `null` for an unknown name is the
  normal case, not an error.
- **Exceptions propagate**: I/O faults MUST throw rather than return `null`. The registry
  wraps the thrown exception in `ShellBlueprintUnavailableException` for callers.
- **`ListAsync` stability**: given an unchanged catalogue, calling `ListAsync` with
  `cursor = null` repeatedly MUST yield the same first page. The overall enumeration
  order MUST be deterministic (e.g., ordered by name) so paging is resumable.
- **Cursor format is opaque**: callers treat `NextCursor` as an opaque string.
  Implementations MAY choose any encoding (last-name, offset, continuation token, etc.)
  as long as it round-trips faithfully.
- **Stateless lookups**: `GetAsync`, `ExistsAsync`, and `ListAsync` MUST be safe to call
  concurrently; implementations MUST NOT store per-call state in instance fields.

## Relationship to `IShellBlueprintManager`

A provider that wraps a mutable source MAY construct its `ProvidedBlueprint` results
with a `Manager` reference attached. A provider MAY be the same type as its manager
(implementing both interfaces), or it MAY delegate to a separate manager instance. The
registry does NOT scan `IEnumerable<IShellBlueprintManager>` independently for
discovery — the provider is the authoritative source of manager association.

## Reference implementations (in this feature)

| Type | Source | Manager? |
|------|--------|----------|
| `InMemoryShellBlueprintProvider` | `AddShell(...)` delegates in composition root | Optional — see builder API |
| `ConfigurationShellBlueprintProvider` | `appsettings.json` / external config files | No (read-only) |
| `CompositeShellBlueprintProvider` | Wraps `IEnumerable<IShellBlueprintProvider>` | Delegates |
| `FluentStorageShellBlueprintProvider` | Blob container (FluentStorage) | Yes |

## Testing surface

The feature ships:
- Unit tests covering null/exception paths in each reference implementation.
- A `StubShellBlueprintProvider` test helper (in `tests/CShells.Tests/TestHelpers/`) that
  accepts a `Dictionary<string, IShellBlueprint>` and deterministic iteration order so
  integration tests can control catalogue state precisely.
