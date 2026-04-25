# Contract: `IShellRegistry` (delta from feature 007)

**Feature**: [008-single-provider-simplification](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/IShellRegistry.cs`

`IShellRegistry`'s **public** contract is unchanged. Every member shipped in
`007` retains its signature and its specified behavior. This document records
the only delta: an internal constructor argument shape change.

## Public surface

No changes. `GetOrActivateAsync`, `ActivateAsync`, `ReloadAsync`,
`ReloadActiveAsync`, `DrainAsync`, `UnregisterBlueprintAsync`,
`GetBlueprintAsync`, `GetManagerAsync`, `ListAsync`, `GetActive`, `GetAll`,
`GetActiveShells`, `Subscribe`, `Unsubscribe` — all unchanged.

## Internal constructor

The default implementation `ShellRegistry` changes its constructor parameter
type:

| | Before (007) | After (008) |
|---|---|---|
| First parameter | `CompositeShellBlueprintProvider blueprintProvider` | `IShellBlueprintProvider blueprintProvider` |

The registry no longer requires the composite — it depends on the abstraction
directly. Callers (the DI factory in `ServiceCollectionExtensions.AddCShells`)
supply whichever single provider was selected by the composition-time guard
(FR-005, FR-006).

## Removed dependencies

`DuplicateBlueprintException` is no longer referenced anywhere in the registry.
The internal `ShouldWrapAsUnavailable` helper drops its
`DuplicateBlueprintException` exclusion.

## Failure semantics

Unchanged from `007`:

- Provider returns `null` → `ShellBlueprintNotFoundException` (404 in middleware).
- Provider throws → wrapped as `ShellBlueprintUnavailableException` (503 in
  middleware), with the original exception as `InnerException`.
- Initializer throws during activation → propagates raw; partial provider
  disposed.
- Manager throws during `UnregisterBlueprintAsync` → propagates raw.

## Composition-time guard (new — enforced in DI, not in this interface)

The framework's DI registration code enforces FR-005 and FR-006. When the host
constructs the service provider (or first resolves `IShellRegistry`):

- If `AddBlueprintProvider(...)` was called more than once →
  `InvalidOperationException` ("exactly one external provider is permitted").
- If `AddShell(...)` AND `AddBlueprintProvider(...)` were both called →
  `InvalidOperationException` whose message:
  - explains that `AddShell` registers blueprints with the in-memory provider
    and `AddBlueprintProvider`-registered providers are external;
  - states that exactly one provider is permitted;
  - enumerates the three valid resolutions (move blueprints into the external
    source, drop the external provider, or implement a custom combining
    `IShellBlueprintProvider`).

The guard is the framework's responsibility, not the registry's. The registry
is constructed only after the guard succeeds.
