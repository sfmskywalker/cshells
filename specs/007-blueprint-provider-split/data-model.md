# Phase 1 Data Model: Scale-Ready Blueprint Provider/Manager Split

**Feature**: [007-blueprint-provider-split](spec.md)
**Date**: 2026-04-24

This document enumerates the entities the feature introduces or modifies, their
relationships, validation rules, and (where applicable) state transitions. Interface
contracts are specified in [`contracts/`](contracts/).

## 1. New entities

### 1.1 `ProvidedBlueprint`

A record pairing a blueprint with its owning manager (if any). Returned by
`IShellBlueprintProvider.GetAsync`.

| Field | Type | Rules |
|-------|------|-------|
| `Blueprint` | `IShellBlueprint` | Required; non-null. |
| `Manager` | `IShellBlueprintManager?` | Optional. `null` ⇒ the blueprint's source is read-only. |

Invariants:
- When `Manager` is non-null, `Manager.Owns(Blueprint.Name)` MUST return `true`.
- `Blueprint.Name` is case-insensitive ordinal; the composite provider uses this for
  duplicate detection.

### 1.2 `BlueprintListQuery`

Paging and filtering input for `IShellBlueprintProvider.ListAsync`.

| Field | Type | Default | Rules |
|-------|------|---------|-------|
| `Cursor` | `string?` | `null` | Opaque; `null` requests the first page. |
| `Limit` | `int` | `50` | Range `[1, 500]`. Guarded. |
| `NamePrefix` | `string?` | `null` | Case-insensitive ordinal prefix match. |

### 1.3 `BlueprintPage`

Output of `IShellBlueprintProvider.ListAsync`.

| Field | Type | Rules |
|-------|------|-------|
| `Items` | `IReadOnlyList<BlueprintSummary>` | Required; may be empty. |
| `NextCursor` | `string?` | `null` on the final page; otherwise opaque. |

Invariants:
- `Items.Count <= Limit` from the query.
- When `NextCursor` is `null`, passing it (or the literal `null`) in a subsequent query
  MUST yield the first page again — i.e., `null` has the same meaning at the tail as at
  the head. (In practice callers stop iterating when `NextCursor` is `null`.)

### 1.4 `BlueprintSummary`

Per-blueprint row returned by listing. Minimal to keep paging cheap.

| Field | Type | Rules |
|-------|------|-------|
| `Name` | `string` | Required; non-empty. |
| `SourceId` | `string` | Required; stable provider identifier (typically the provider type's short name). Same provider always returns the same value. |
| `Mutable` | `bool` | `true` iff the owning provider has a manager for this name. |
| `Metadata` | `IReadOnlyDictionary<string, string>` | Provider-defined; may be empty. |

### 1.5 `ShellListQuery`

Paging and filtering input for `IShellRegistry.ListAsync`. Superset of
`BlueprintListQuery` that adds lifecycle filtering.

| Field | Type | Default | Rules |
|-------|------|---------|-------|
| `Cursor` | `string?` | `null` | As `BlueprintListQuery`. |
| `Limit` | `int` | `50` | Range `[1, 500]`. |
| `NamePrefix` | `string?` | `null` | As `BlueprintListQuery`. |
| `StateFilter` | `ShellLifecycleState?` | `null` | When set, only active shells in the given state are returned; inactive blueprints are skipped entirely. |

### 1.6 `ShellPage`

Output of `IShellRegistry.ListAsync`.

| Field | Type | Rules |
|-------|------|-------|
| `Items` | `IReadOnlyList<ShellSummary>` | Required; may be empty. |
| `NextCursor` | `string?` | As `BlueprintPage.NextCursor`. |

### 1.7 `ShellSummary`

Per-shell row returned by the registry. Extends `BlueprintSummary` with lifecycle state.

| Field | Type | Rules |
|-------|------|-------|
| `Name` | `string` | Required. |
| `SourceId` | `string` | Required. |
| `Mutable` | `bool` | From the provider. |
| `ActiveGeneration` | `int?` | `null` iff no active shell for this name. |
| `State` | `ShellLifecycleState?` | `null` iff no active shell. |
| `ActiveScopeCount` | `int` | `0` iff no active shell; otherwise the count at the time the page was assembled. |
| `LastScopeOpenedAt` | `DateTimeOffset?` | `null` iff no active shell or no scope has opened since activation. |
| `Metadata` | `IReadOnlyDictionary<string, string>` | From the provider. |

Invariants:
- When any of `ActiveGeneration`, `State`, `LastScopeOpenedAt` is null, all three are
  null (there is no active shell).

### 1.8 `ReloadOptions`

Configuration for `IShellRegistry.ReloadActiveAsync`.

| Field | Type | Default | Rules |
|-------|------|---------|-------|
| `MaxDegreeOfParallelism` | `int` | `8` | Range `[1, 64]`. Guarded. |

## 2. New exceptions

All exceptions live in `CShells.Abstractions/Lifecycle/` and carry structured context.

| Type | Extends | Context properties |
|------|---------|-------------------|
| `ShellBlueprintNotFoundException` | `InvalidOperationException` | `Name` |
| `ShellBlueprintUnavailableException` | `InvalidOperationException` | `Name`; inner exception MUST be the provider's original fault. |
| `BlueprintNotMutableException` | `InvalidOperationException` | `Name`; optional `SourceId` when known. |
| `DuplicateBlueprintException` | `InvalidOperationException` | `Name`, `FirstProviderType`, `SecondProviderType`. |

All exception messages MUST include actionable guidance per Principle IV (e.g., "Did
you forget to register a blueprint manager for the source that owns this name?").

## 3. Modified entities

### 3.1 `IShellRegistry`

Surface change; details in [`contracts/IShellRegistry.md`](contracts/IShellRegistry.md).
The in-memory blueprint dictionary (feature `006`'s `NameSlot.Blueprint`) is removed;
the registry consults the composite provider for lookup and caches only the live
active generation.

### 3.2 `NameSlot` (internal to `ShellRegistry`)

| Field | Change | Reason |
|-------|--------|--------|
| `Blueprint` | REMOVED | Blueprint is owned by the provider, not the registry. |
| `NextGeneration` | unchanged | Still used to stamp new shells. |
| `Semaphore` | unchanged | Still used to serialize activation / reload / unregister per name. |
| `ActiveShell` | unchanged | Still holds the single live generation. |

### 3.3 `CShellsBuilder` (extension methods)

| Method | Status | Notes |
|--------|--------|-------|
| `AddShell(name, configure)` | MODIFIED | Routes to the singleton `InMemoryShellBlueprintProvider`; no longer calls `registry.RegisterBlueprint`. |
| `AddShellsFromConfiguration(section)` | MODIFIED | Registers a `ConfigurationShellBlueprintProvider` bound to the section. |
| `PreWarmShells(params string[] names)` | NEW | Records names to activate in `StartAsync`; no-op at build time. |

## 4. Relationships

```text
IShellRegistry ──reads──▶ IShellBlueprintProvider (composite)
                          │
                          ├─ InMemoryShellBlueprintProvider  (from AddShell delegates)
                          ├─ ConfigurationShellBlueprintProvider  (from Shells/*.json)
                          └─ FluentStorageShellBlueprintProvider  (ALSO IShellBlueprintManager)

IShellRegistry ──holds──▶ NameSlot[name]
                          ├─ Semaphore (serializes activation/reload/unregister)
                          ├─ NextGeneration
                          └─ ActiveShell

ProvidedBlueprint ──pairs──▶ IShellBlueprint + optional IShellBlueprintManager
```

Key invariants across relationships:

1. **One owning provider per name**: exactly one provider in the composite returns a
   non-null `ProvidedBlueprint` for a given name. Two or more raise
   `DuplicateBlueprintException` when detected.
2. **Manager ownership is provider-declared**: the manager returned by `GetManager(name)`
   is the one the owning provider attached to its `ProvidedBlueprint`, or `null`.
3. **Active shell lifecycle is registry-owned**: providers and managers do not hold
   references to `IShell`; mutation operations return after the underlying store is
   updated, and the registry independently drives drain + dispose via the existing
   feature-`006` drain machinery.

## 5. Cursor codec (composite)

Informative pseudo-schema for the composite cursor, base64-encoded UTF-8 of:

```json
{
  "v": 1,
  "entries": [
    { "p": 0, "c": "<sub-provider-0 cursor>" },
    { "p": 2, "c": "<sub-provider-2 cursor>" }
  ]
}
```

- `v` is the codec version. Changes require a bump and documented migration.
- `p` is the sub-provider's DI-registration index within the composite (0-based).
- Providers whose iteration is complete are omitted from `entries`.
- An empty `entries` array is never produced; when every sub-provider is exhausted, the
  composite emits `NextCursor = null` instead.

## 6. State transitions (unchanged)

Feature `007` introduces no new lifecycle states. All existing transitions from feature
`006` (`Initializing → Active → Deactivating → Draining → Drained → Disposed`) and their
concurrency guarantees apply unchanged. `GetOrActivateAsync` drives the same
`Initializing → Active` transition as explicit `ActivateAsync` does; `UnregisterBlueprintAsync`
drives the same drain-and-dispose sequence as `DrainAsync` does.
