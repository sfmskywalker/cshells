# Phase 1 Data Model: Single-Provider Blueprint Simplification

**Feature**: [008-single-provider-simplification](spec.md)
**Date**: 2026-04-25

The simplification deletes types and changes one DI binding shape. It does not
introduce new entities. This document records the delta from feature `007`.

## 1. Deleted entities

| Type | Project | Reason |
|------|---------|--------|
| `CompositeShellBlueprintProvider` | `CShells/Lifecycle/Providers/` | Multi-provider composition retired |
| `CompositeProviderOptions` | `CShells/Lifecycle/Providers/` | No composite to configure |
| `CompositeCursorCodec` (internal) | `CShells/Lifecycle/Providers/` | No multi-provider cursor to encode |
| `CompositeCursorEntry` (internal) | `CShells/Lifecycle/Providers/` | Codec helper |
| `DuplicateBlueprintException` | `CShells.Abstractions/Lifecycle/` | Error condition no longer reachable |

## 2. Modified entities

### 2.1 `ShellRegistry` (constructor)

**Before (007):**

```csharp
public ShellRegistry(
    CompositeShellBlueprintProvider blueprintProvider,
    ShellProviderBuilder? providerBuilder = null,
    IServiceProvider? rootProvider = null,
    ILogger<ShellRegistry>? logger = null,
    IEnumerable<IShellLifecycleSubscriber>? subscribers = null)
```

**After (008):**

```csharp
public ShellRegistry(
    IShellBlueprintProvider blueprintProvider,   // ← any implementation
    ShellProviderBuilder? providerBuilder = null,
    IServiceProvider? rootProvider = null,
    ILogger<ShellRegistry>? logger = null,
    IEnumerable<IShellLifecycleSubscriber>? subscribers = null)
```

The registry depends on the abstraction directly. The composite is no longer
required to fan out — there is only one provider.

### 2.2 `ShellRegistry.ShouldWrapAsUnavailable`

The `DuplicateBlueprintException` exclusion is removed (the type no longer
exists). The remaining filter:

```csharp
private static bool ShouldWrapAsUnavailable(Exception ex) =>
    ex is not ShellBlueprintNotFoundException &&
    ex is not OperationCanceledException;
```

### 2.3 `CShellsBuilder` (state — unchanged shape, semantics tightened)

| Field | Status | Notes |
|-------|--------|-------|
| `_inlineBlueprints` | unchanged | Populated by `AddShell` / `AddBlueprint` |
| `_providerFactories` | unchanged shape | Now MUST contain at most one entry; second `AddBlueprintProvider` call throws via FR-006 |
| `_preWarmNames` | unchanged | Independent of provider |

### 2.4 `ServiceCollectionExtensions.AddCShells` DI bindings

| Binding | Before (007) | After (008) |
|---------|--------------|-------------|
| `InMemoryShellBlueprintProvider` (singleton) | registered always; populated from `InlineBlueprints` | NOT registered as a distinct service; constructed inside the factory below when chosen |
| `CompositeShellBlueprintProvider` (singleton) | registered always | DELETED |
| `IShellBlueprintProvider` (singleton) | resolves to `CompositeShellBlueprintProvider` | resolves to either the in-memory provider (default) OR the factory-supplied external provider; throws on conflict per FR-005/FR-006 |

## 3. Unchanged entities (carry forward from 007)

- `IShellBlueprintProvider` interface — open extension point. Lazy `GetAsync`,
  optional `ExistsAsync`, paginated `ListAsync`. Now the only DI binding for
  blueprint sourcing.
- `IShellBlueprintManager` interface — unchanged.
- `ProvidedBlueprint` record — unchanged.
- `BlueprintListQuery` / `BlueprintPage` / `BlueprintSummary` — unchanged.
- `ShellListQuery` / `ShellPage` / `ShellSummary` — unchanged.
- `ReloadOptions` — unchanged.
- `ShellBlueprintNotFoundException` — unchanged.
- `ShellBlueprintUnavailableException` — unchanged.
- `BlueprintNotMutableException` — unchanged.
- `InMemoryShellBlueprintProvider` — unchanged.
- `ConfigurationShellBlueprintProvider` — unchanged.
- `FluentStorageShellBlueprintProvider` — unchanged.

## 4. Relationships

```text
IShellRegistry ──reads──▶ IShellBlueprintProvider (single instance)
                          │
                          ├─ InMemoryShellBlueprintProvider          (default)
                          ├─ ConfigurationShellBlueprintProvider     (via WithConfigurationProvider)
                          ├─ FluentStorageShellBlueprintProvider     (via WithFluentStorageBlueprints)
                          └─ <any third-party impl>                  (via AddBlueprintProvider)

IShellRegistry ──holds──▶ NameSlot[name]
                          ├─ Semaphore (serializes activation/reload/unregister)
                          ├─ NextGeneration
                          └─ ActiveShell
```

Compared with `007`'s diagram, the composite layer is gone. The registry talks
to whichever single provider is registered.

## 5. State transitions

Unchanged from `007` (and `006`). Lifecycle states
(`Initializing → Active → Deactivating → Draining → Drained → Disposed`) and all
their concurrency guarantees apply identically.

## 6. Composition-time guard semantics

The DI factory that produces `IShellBlueprintProvider` enforces:

1. `_providerFactories.Count > 1` → `InvalidOperationException` (FR-006).
2. `_inlineBlueprints.Count > 0 && _providerFactories.Count == 1` →
   `InvalidOperationException` with the FR-005 teaching message.
3. `_providerFactories.Count == 1` → return `_providerFactories[0](sp)`.
4. Otherwise → return `new InMemoryShellBlueprintProvider(_inlineBlueprints)`
   (which may be empty — that's legal).

The factory runs once per host (singleton lifetime). The guard fires before any
shell is activated, which means before any HTTP traffic flows.
