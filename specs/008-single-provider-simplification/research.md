# Phase 0 Research: Single-Provider Blueprint Simplification

**Feature**: [008-single-provider-simplification](spec.md)
**Date**: 2026-04-25

The simplification has a small design surface. Three open questions; all
resolved without `NEEDS CLARIFICATION` markers.

## R-001: Where to enforce the fail-fast guard (FR-005, FR-006)

**Decision**: Enforce in the DI factory that produces `IShellBlueprintProvider`.
The factory captures the builder's `InlineBlueprints.Count` and `ProviderFactories.Count`
at the moment the registry first resolves its dependency (transitively, when the
service provider is built or when `IShellRegistry` is first requested). If both
counts are non-zero, throw the FR-005 error. If `ProviderFactories.Count > 1`,
throw the FR-006 error.

**Rationale**:
- The startup hosted service (`CShellsStartupHostedService`) resolves
  `IShellRegistry` during `StartAsync`, which guarantees the guard fires before
  any HTTP traffic is served.
- Throwing in the DI factory surfaces a clean `InvalidOperationException` at the
  exact construction site — no separate guard hosted service needed.
- The builder cannot enforce at registration time because `AddShell` and
  `AddBlueprintProvider` may be called in any order and the host might still
  intend to remove one of them before container construction. Deferring to DI
  resolution is the only safe place.

**Alternatives considered**:
- **Throw immediately when the conflicting call is made on the builder**:
  rejected — would require ordering constraints (you'd have to call
  `AddBlueprintProvider` first then never `AddShell`) and would surprise hosts
  that conditionally register based on configuration.
- **Validate in a separate `IHostedService` that runs at startup**: rejected —
  adds a second hosted service for what is fundamentally a DI-construction
  concern, and the failure would manifest after container build instead of at it.

## R-002: How `AddShell` and `AddBlueprintProvider` track each other

**Decision**: The builder holds two existing collections — `InlineBlueprints`
(list of `IShellBlueprint`) and `ProviderFactories` (list of
`Func<IServiceProvider, IShellBlueprintProvider>`). The DI factory inspects both
counts. No new state is added to the builder.

**Rationale**:
- These collections already exist from feature 007. Reusing them keeps the
  builder surface unchanged.
- `AddShell` adds to `InlineBlueprints` (already implemented).
  `AddBlueprintProvider` adds to `ProviderFactories` (already implemented).
- The guard is a derived property: `InlineBlueprints.Any() && ProviderFactories.Any()`.

**Alternatives considered**:
- **Add an `enum BlueprintSource { None, InMemory, External }` to the builder**:
  rejected — adds state for a derived condition that's already trivially
  computable.

## R-003: How to register the single `IShellBlueprintProvider` in DI

**Decision**: The DI factory branches:

```csharp
services.TryAddSingleton<IShellBlueprintProvider>(sp =>
{
    var hasInline = builder.InlineBlueprints.Count > 0;
    var externalCount = builder.ProviderFactories.Count;

    if (externalCount > 1)
        throw new InvalidOperationException("…exactly one external provider…");
    if (hasInline && externalCount == 1)
        throw new InvalidOperationException(
            "AddShell registers blueprints with the in-memory provider, but " +
            "AddBlueprintProvider-registered providers are external — and " +
            "exactly one provider is permitted. Either: …");

    if (externalCount == 1)
        return builder.ProviderFactories[0](sp);

    // Default path: in-memory provider, populated from AddShell calls (which
    // may be empty — that's a legal "no blueprints anywhere" host).
    return new InMemoryShellBlueprintProvider(builder.InlineBlueprints);
});
```

The `InMemoryShellBlueprintProvider` singleton is no longer registered as a
distinct DI service — it's only constructed when chosen. `ServiceCollectionExtensions`
also no longer registers `CompositeShellBlueprintProvider` at all.

**Rationale**:
- Single source of truth: `IShellBlueprintProvider` is the only DI binding for
  blueprint sourcing.
- The factory naturally enforces the guard at the right time.
- Tests that need to add blueprints dynamically to the in-memory provider after
  DI build will be migrated: instead of `host.GetRequiredService<InMemoryShellBlueprintProvider>().Add(...)`,
  they'll add via the builder's `AddShell` (or use a stub provider directly).

**Alternatives considered**:
- **Keep `InMemoryShellBlueprintProvider` registered as its own type so tests
  can resolve it**: rejected — couples test ergonomics to production DI shape.
  Tests that need post-build mutation should use `StubShellBlueprintProvider`
  registered via `AddBlueprintProvider`, which is more honest about what they're
  testing.

## Summary

All three decisions resolved without ambiguity. Phase 1 (data model + contracts)
proceeds.
