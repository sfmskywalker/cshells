# Phase 1 Data Model: Shell Management REST API

**Feature**: [009-management-api](spec.md)
**Date**: 2026-04-27

This feature introduces one new abstraction property, one new project's
worth of internal DTOs, and removes one private collection from the
registry. No new public exception types, no schema migrations, no DI
service registrations.

## 1. New / modified abstraction surface

### 1.1 `IShell.Drain` (new property — `CShells.Abstractions/Lifecycle/IShell.cs`)

```csharp
/// <summary>
/// Gets the in-flight drain operation associated with this generation, or <c>null</c>
/// when no drain is in flight.
/// </summary>
/// <remarks>
/// <para>
/// The value is non-null exactly when <see cref="State"/> is one of
/// <see cref="ShellLifecycleState.Deactivating"/>, <see cref="ShellLifecycleState.Draining"/>,
/// or <see cref="ShellLifecycleState.Drained"/>; null when the state is
/// <see cref="ShellLifecycleState.Initializing"/>, <see cref="ShellLifecycleState.Active"/>,
/// or <see cref="ShellLifecycleState.Disposed"/>.
/// </para>
/// <para>
/// The reference returned is the same instance any concurrent caller of
/// <see cref="IShellRegistry.DrainAsync"/> would receive for this shell — exposing it
/// directly makes per-generation drain observability possible without round-tripping
/// through the registry.
/// </para>
/// </remarks>
IDrainOperation? Drain { get; }
```

### 1.2 `Shell` (modified — `CShells/Lifecycle/Shell.cs`)

Adds:

| Field / Member | Visibility | Purpose |
|---|---|---|
| `private DrainOperation? _drain;` | `private` | Backing field for `Drain` property. |
| `public IDrainOperation? Drain => Volatile.Read(ref _drain);` | `public` | Implements `IShell.Drain`; volatile read for cross-thread visibility, matching the existing `_state` access pattern. |
| `internal DrainOperation PublishDrain(DrainOperation candidate)` | `internal` | CAS-publish: returns the winner. Used by `ShellRegistry.DrainAsync` for publish-once semantics. Idempotent on subsequent calls — concurrent callers all observe the first published instance. |

Concurrency contract for `PublishDrain`:

```csharp
internal DrainOperation PublishDrain(DrainOperation candidate)
{
    var winner = Interlocked.CompareExchange(ref _drain, candidate, null);
    return winner ?? candidate;
}
```

The CAS pattern is identical to the existing `_disposeTask` publish-once
pattern in the same file.

### 1.3 `ShellRegistry.DrainAsync` (modified — `CShells/Lifecycle/ShellRegistry.cs`)

**Before:**

```csharp
private readonly ConcurrentDictionary<IShell, Lazy<DrainOperation>> _drainOps = new();

public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken ct = default)
{
    Guard.Against.Null(shell);
    if (shell is not Shell typedShell) throw new ArgumentException(...);

    var lazy = _drainOps.GetOrAdd(shell, s => new Lazy<DrainOperation>(
        () => StartDrain((Shell)s),
        LazyThreadSafetyMode.ExecutionAndPublication));
    return Task.FromResult<IDrainOperation>(lazy.Value);
}
```

**After:**

```csharp
public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken ct = default)
{
    Guard.Against.Null(shell);
    if (shell is not Shell typedShell) throw new ArgumentException(...);

    if (typedShell.Drain is { } existing)
        return Task.FromResult(existing);

    var policy = ResolveDrainPolicy();
    var gracePeriod = ResolveGracePeriod();
    var candidate = new DrainOperation(typedShell, policy, gracePeriod, ResolveDrainLogger());
    var winner = typedShell.PublishDrain(candidate);
    if (ReferenceEquals(winner, candidate))
        StartDrainRun(typedShell, candidate);
    return Task.FromResult<IDrainOperation>(winner);
}
```

The `_drainOps` field, its dictionary management code, and the `Lazy<>`
allocation pattern are deleted. `StartDrainRun` keeps the existing
`ForceAdvanceAsync(Draining)` + run continuation logic minus the
dictionary-cleanup `ContinueWith` (no dictionary to clean up).

## 2. New internal DTOs (`CShells.Management.Api/Models/`)

All types are `internal sealed record`. Field names follow camelCase (System.Text.Json
`JsonSerializerDefaults.Web` is the host's default for Minimal API). No
attributes are necessary; record positional parameters serialize by name.

### 2.1 `DrainSnapshot`

```csharp
internal sealed record DrainSnapshot(
    string Status,                  // DrainStatus.ToString() — "Pending" / "Completed" / "TimedOut" / "Forced"
    DateTimeOffset? Deadline);      // null for unbounded policies
```

Mapped from `IDrainOperation.Status` + `IDrainOperation.Deadline`.

### 2.2 `ShellGenerationResponse`

```csharp
internal sealed record ShellGenerationResponse(
    int Generation,
    string State,                   // ShellLifecycleState.ToString()
    DateTimeOffset CreatedAt,
    DrainSnapshot? Drain);          // populated iff State is Deactivating/Draining/Drained
```

Mapped from `IShell.Descriptor.Generation`, `IShell.State`,
`IShell.Descriptor.CreatedAt`, and `IShell.Drain` per FR-005.

### 2.3 `BlueprintResponse`

```csharp
internal sealed record BlueprintResponse(
    string Name,
    IReadOnlyList<string> Features,
    IReadOnlyDictionary<string, string> ConfigurationData);  // verbatim per FR-012a
```

`ConfigurationData` is included verbatim, regardless of key names —
authorization is the host's responsibility (FR-014). The XML doc comment
on `MapShellManagementApi` flags this exposure explicitly.

### 2.4 `ShellListItem`

```csharp
internal sealed record ShellListItem(
    string Name,
    BlueprintResponse? Blueprint,        // populated iff a blueprint is currently registered
    ShellGenerationResponse? Active);    // null when no active generation; the active gen only
                                         // (non-active gens are visible via GET /{name})
```

### 2.5 `ShellPageResponse`

```csharp
internal sealed record ShellPageResponse(
    IReadOnlyList<ShellListItem> Items,
    string? NextCursor,
    int PageSize);
```

Maps `ShellPage.Items` (`ShellSummary`) + `ShellPage.NextCursor`.

### 2.6 `ShellDetailResponse`

```csharp
internal sealed record ShellDetailResponse(
    string Name,
    BlueprintResponse? Blueprint,
    IReadOnlyList<ShellGenerationResponse> Generations);
```

`Generations` includes the active generation **and** every still-held
deactivating/draining/drained generation — the array `IShellRegistry.GetAll(name)`
returns. Each entry's `Drain` field is populated for non-active states
per FR-005.

### 2.7 `ReloadResultResponse`

```csharp
internal sealed record ReloadResultResponse(
    string Name,
    bool Success,
    ShellGenerationResponse? NewShell,
    DrainSnapshot? Drain,
    ErrorDescription? Error);

internal sealed record ErrorDescription(
    string Type,        // exception type name without namespace
    string Message);    // exception message
```

Mapped from `ReloadResult` (`Name`, `NewShell`, `Drain`, `Error`).
`Success = Error is null && NewShell is not null`.

### 2.8 `DrainResultResponse`

```csharp
internal sealed record DrainResultResponse(
    string Name,
    int Generation,
    string Status,
    TimeSpan ScopeWaitElapsed,
    int AbandonedScopeCount,
    IReadOnlyList<DrainHandlerResultResponse> HandlerResults);

internal sealed record DrainHandlerResultResponse(
    string HandlerType,
    string Outcome,             // mapped from DrainHandlerResult fields
    TimeSpan Elapsed,
    string? ErrorMessage);
```

Mapped from `DrainResult` (which carries `Shell.Descriptor` + status +
scope-wait + handler results).

## 3. Endpoint-handler contract summary

For each route, the handler's input → registry call → response mapping:

| Route | Registry call | Response shape | Status mapping |
|---|---|---|---|
| `GET /` | `ListAsync(query)` | `ShellPageResponse` (200) | unavailable → 503; cancel → 503 |
| `GET /{name}` | `GetBlueprintAsync(name)` + `GetAll(name)` | `ShellDetailResponse` (200) | unknown → 404; unavailable → 503 |
| `GET /{name}/blueprint` | `GetBlueprintAsync(name)` | `BlueprintResponse` (200) | unknown → 404; unavailable → 503 |
| `POST /reload/{name}` | `ReloadAsync(name, ct)` | `ReloadResultResponse` (200) | not-found → 404; unavailable → 503; cancel → 503 |
| `POST /reload-all?maxDegreeOfParallelism=N` | `ReloadActiveAsync(opts, ct)` | `ReloadResultResponse[]` (200) | parallelism out-of-range → 400; cancel → 503 |
| `POST /{name}/force-drain` | `GetAll(name)` + parallel `ForceAsync`+`WaitAsync` per drain | `DrainResultResponse[]` (200) | unknown name → 404; no in-flight drain → 404; cancel → 503 |

Per FR-013, all 4xx/5xx responses use `Results.Problem(...)` (RFC 7807).

## 4. Deleted entities

| Entity | Project | Reason |
|---|---|---|
| `ShellRegistry._drainOps` (private field) | `CShells/Lifecycle/` | Replaced by `IShell.Drain` (R-001/R-002) |
| Inline `app.MapGet("/_shells/status", ...)` | `samples/CShells.Workbench/Program.cs` | Superseded by `GET /_admin/shells/` (R-008) |

## 5. State transitions

`IShell.Drain` is bound to lifecycle state per the FR-004 invariant:

```text
State                Drain
───────────────────  ──────────────
Initializing      →  null
Active            →  null
Deactivating      →  non-null (the in-flight DrainOperation)
Draining          →  non-null (same instance)
Drained           →  non-null (same instance, terminal status)
Disposed          →  null
```

Implementation invariant: `Shell.PublishDrain(...)` is called by
`ShellRegistry.DrainAsync` exactly when the registry initiates the drain
on a shell — which is the same moment the registry would have populated
`_drainOps` in the previous code path. No additional state changes
required.

The `Disposed → null` row is an explicit reset: when `Shell.DisposeAsync`
runs, `_drain` is cleared (set back to null) so the test invariant in
`ShellDrainPropertyTests` can assert a clean post-disposal observation.
This also breaks the reference cycle (`Shell` → `DrainOperation` → `Shell`)
on disposal, helping GC.

## 6. Relationships

```text
IShellRegistry ──reads──▶ IShellBlueprintProvider (single instance, from 008)
   │
   └──holds──▶ NameSlot[name]
                 ├─ Semaphore (serializes activation/reload/unregister)
                 ├─ NextGeneration
                 └─ All:[IShell, IShell, ...]   (active + non-active gens)
                       │
                       └─ IShell.Drain ──▶ IDrainOperation? (new — non-null iff Deactivating/Draining/Drained)

CShells.Management.Api endpoints
   │
   ├──reads──▶ IShellRegistry (via HttpContext.RequestServices = root provider)
   ├──reads──▶ IShell.Drain  (per-generation, via GetAll(name) walk)
   └──maps──▶ internal DTOs ──▶ JSON response
```

Compared with `008`'s diagram: same registry shape, plus a new
per-generation drain reference visible on every `IShell`.

## 7. Validation rules

The package defers all data validation to existing models:

- `name` route segment: passed through to registry; registry's existing
  `Guard.Against.NullOrWhiteSpace` produces an `ArgumentException` if
  empty (which never happens — Minimal API route binding rejects empty
  segments before the handler runs).
- `pageSize` query: `ShellListQuery`'s constructor validates; out-of-range
  surfaces as `ArgumentOutOfRangeException` and is mapped to 400.
- `maxDegreeOfParallelism` query: `ReloadOptions.EnsureValid()` validates;
  out-of-range surfaces as `ArgumentOutOfRangeException` and is mapped to
  400.
- `cursor` query: opaque string; the registry's pagination decoder
  validates and surfaces format errors as registry exceptions (caught and
  mapped per FR-013).
