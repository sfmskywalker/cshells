# Phase 0 Research: Blueprint-Aware Path Routing

**Feature**: [010-blueprint-aware-routing](spec.md)
**Date**: 2026-04-28

This document resolves the open design questions that Phase 1 (data model + contracts) needs settled before it can proceed. Each section records the decision, the rationale, and the alternatives that were considered and rejected.

## R-001: Index population strategy per routing mode

**Decision**: Hybrid. The route index uses two complementary lookup paths depending on the routing mode of the request:

- **Path mode with a non-empty path segment** (the URL's first segment is the candidate blueprint name): the index does a single `IShellBlueprintProvider.GetAsync(segment)` call on the request hot path. **No reverse-lookup table is populated.** This is the common case for path-routed multi-tenant deployments and preserves `007`'s O(1)-per-tenant scaling.
- **Root-path mode (`Path = ""`), Host mode, Header mode, Claim mode**: the routing identifier cannot be derived from the URL alone — the index must reverse-look-up from configuration value to blueprint name. The index lazily populates a `FrozenDictionary<RoutingKey, ShellRouteEntry>` snapshot on **first use of any of these modes**, by streaming `IShellBlueprintProvider.ListAsync(...)` once. Subsequent requests use the snapshot in O(1). Lifecycle events (`ShellAdded`/`ShellRemoved`/`ShellReloaded`) update the snapshot incrementally without a full re-scan.

**Rationale**:
- Pure path-by-name routing — the configuration the spec's User Story 1 exemplifies — pays exactly one provider call per cold blueprint. No upfront cost, no scanning, no surprises at 100k tenants.
- Modes where the routing key is decoupled from the blueprint name fundamentally need a reverse map. Building that map on first use (rather than at startup) preserves `007`'s SC-001 scaling for hosts that never use those modes, and amortises the cost over the host's lifetime for hosts that do.
- Lifecycle-driven incremental updates avoid re-scanning the catalogue on every blueprint change. A blueprint addition adds one entry; a removal removes one; a reload replaces one.

**Alternatives considered**:
- **Always pre-populate the full reverse map at startup**: re-introduces O(catalogue) startup cost. Violates the spec's User Story 4 / SC-004 scaling promise and contradicts the `007` design intent.
- **Always lookup-by-name only** (treat host/header/claim as candidate names): works only when the configuration values happen to equal blueprint names. The `WebRouting:Host = "acme.example.com"` case where the host is *not* the blueprint name would silently fail to route. Rejected.
- **Demand-driven reverse map per request** (call `provider.GetAsync` for every blueprint until one matches): O(catalogue) per request hot path. Unacceptable.

## R-002: Snapshot consistency mechanism

**Decision**: The route index holds an immutable `ShellRouteIndexSnapshot` reference behind `Volatile.Read`/`Volatile.Write`. Readers (`WebRoutingShellResolver`) call a single `index.TryMatch(criteria)` method that performs `Volatile.Read` of the snapshot and returns immediately. Writers (lifecycle invalidation) construct a new snapshot off-thread inside a `SemaphoreSlim(1,1)`-serialized critical section, then `Volatile.Write` to publish.

The snapshot itself is built around `FrozenDictionary<TKey, ShellRouteEntry>` (one per routing mode). `FrozenDictionary` is available on `net8.0+` and gives O(1) reads with no per-call allocation. For `net8.0` and `net9.0` targets it is in `Microsoft.Collections.Immutable.Frozen` (built into the runtime).

**Rationale**:
- Snapshot publication via volatile reference swap satisfies FR-013 (readers see either old or new, never partial) without taking any lock on the read path.
- Per-mode `FrozenDictionary` keeps the read O(1) and avoids per-request allocations.
- The semaphore-serialized rebuild path is correct under concurrent invalidation events (e.g., a burst of `ShellAdded` notifications) without forcing every reader through a critical section.

**Alternatives considered**:
- **`ImmutableDictionary` with builder pattern**: works but has higher constant factors than `FrozenDictionary` for read-heavy workloads. Rejected.
- **`ConcurrentDictionary` with no snapshot**: risks readers observing partial state during a multi-key update (e.g., a reload that changes both `Path` and `Host` for the same blueprint). Rejected.
- **`ReaderWriterLockSlim`**: introduces a lock on the read path and is not async-safe. Rejected per Principle VII.

## R-003: Lifecycle invalidation surface

**Decision**: A new internal `ShellRouteIndexInvalidator` implements `INotificationHandler<ShellAdded>`, `INotificationHandler<ShellRemoved>`, and `INotificationHandler<ShellReloaded>`. It holds an `IShellRouteIndex` reference and forwards each notification to a corresponding incremental update method on the index (`AddOrReplaceAsync(name)`, `RemoveAsync(name)`, `ReplaceAsync(name)` — same shape, distinct semantics for documentation clarity).

The invalidator does NOT subscribe to `ShellActivated` or `ShellDeactivating`. Routing metadata is a property of the blueprint, not of the active generation; activation/deactivation cannot change routing.

**Rationale**:
- Reuses the existing `INotificationHandler<T>` pipeline established in feature `006`. No new lifecycle event type is needed.
- The three event types match the only mutations that can change a blueprint's routing config: addition, removal, and reload (which may change configuration in place).
- Subscriber-isolation guarantee from feature `006` (Principle VII) means the invalidator's exceptions are caught and logged by the dispatcher, so an index-population failure cannot block other subscribers from observing the same notification.

**Alternatives considered**:
- **Polling the provider on a timer**: introduces a knob (poll interval), adds latency to picking up changes, and re-introduces the catalogue-scan cost on every poll. Rejected.
- **Index subscribes directly to `IShellBlueprintProvider` via a new "provider events" interface**: would require a contract change to the provider abstraction, expanding scope. Rejected — the existing lifecycle notifications already cover the events we need.

## R-004: Resolver async migration

**Decision**: `IShellResolverStrategy.Resolve` migrates to `Task<ShellId?> ResolveAsync(ShellResolutionContext, CancellationToken)`. (Concretely a `Task<ShellId?>` rather than `ValueTask<ShellId?>` — see rationale.) `WebRoutingShellResolver`, `DefaultShellResolverStrategy`, and `ShellMiddleware` are updated. The old sync method is removed (not retained as a default-interface-method shim).

**Rationale**:
- The route index's read path is sync-completed in the steady state (`Volatile.Read` of a `FrozenDictionary`). However, the FIRST request that triggers reverse-map population (R-001) MUST run asynchronously because `provider.ListAsync(...)` is `Task<BlueprintPage>`. The resolver therefore needs a path that can suspend.
- `Task<ShellId?>` over `ValueTask<ShellId?>`: the steady-state hot path returns a completed task, but the resolver is not invoked at sub-microsecond cadence and the simplicity of `Task` outweighs the marginal allocation savings of `ValueTask` for a once-per-request call. A future change to `ValueTask` is a non-breaking refinement.
- Keeping a sync `Resolve` overload would force every implementation to choose between blocking on async work (forbidden by Principle VII) or partially implementing the contract. Cleaner to migrate the contract.
- Custom strategies that today do purely-sync work migrate by changing one keyword: `public ShellId? Resolve(...)` → `public Task<ShellId?> ResolveAsync(...) => Task.FromResult<ShellId?>(...)`.

**Alternatives considered**:
- **Keep `Resolve` sync, add `ResolveAsync` as a default interface method that calls `Resolve`**: leaves the sync method as the canonical implementation point. New code wanting async would override `ResolveAsync` and consumers would still be allowed to ignore async. Rejected because (a) it preserves the sync-only-correctness assumption that this feature exists to break, and (b) the routing pipeline still needs to await *something*, so `ShellMiddleware` would still call the async overload — making the sync overload a vestige.
- **Sync `Resolve` plus a side-channel async hook**: more moving parts than the migration is worth. Rejected.

## R-005: Duplicate routing-key policy

**Decision**: When the route index encounters two blueprints whose `WebRouting:*` values would collide (same `Path`, same `Host`, same `HeaderName` value, same `ClaimKey` value, or — separately — both opting into root-path with `Path = ""`), the index applies these rules at population time:

- **Path / Host / HeaderName / ClaimKey collisions**: log a single `Warning`-level entry naming both blueprints and the colliding value. The first-encountered blueprint (in catalogue iteration order, which the provider contract guarantees is deterministic for an unchanged catalogue) wins. The later-encountered blueprint is excluded from the index for *that mode only* — it remains routable via other modes if it has them.
- **Root-path collision (multiple blueprints with `Path = ""`)**: preserve the existing `WebRoutingShellResolver.TryResolveByRootPath` behaviour — the index records the conflict, `TryMatch` returns `null` for root-path queries, and the next resolver strategy decides. This is the behaviour callers already depend on.

The misconfiguration `WebRouting:Path` starting with `/` is detected during entry construction; the entry is excluded from the index, a `Warning` is logged with the blueprint name and bad value, and the existing runtime `InvalidOperationException` from `WebRoutingShellResolver.cs:93` is no longer thrown (the bad path simply doesn't appear in the index).

**Rationale**:
- For unique-value routing modes, deterministic first-wins matches the resolver's pre-existing behaviour (it returned the first active shell whose config matched, in iteration order). The warning surfaces the misconfiguration without breaking routing.
- For root-path mode, the existing "ambiguous → null" semantics is the defensive choice that lets explicit configuration (e.g., a custom resolver with higher priority) win. Preserving it avoids surprising regressions for hosts that intentionally have multiple root-eligible shells gated by another resolver.
- Detecting `Path` starting with `/` at index-population time (rather than throwing on the request hot path) shifts the failure to startup/reload time where it can be diagnosed without a 500-class request leakage.

**Alternatives considered**:
- **Throw on duplicate at population**: blocks the host from starting if a configuration error exists in any blueprint. Rejected — operators should be able to start the host, observe warnings, and fix the misconfiguration without a hard failure.
- **Random tie-break**: rejected — non-deterministic routing is operationally horrible.
- **Last-wins**: rejected — provider iteration order is a contract for the duration of an unchanged catalogue, but blueprints can be added at runtime; last-wins would mean a runtime addition silently steals routing from the prior owner.

## R-006: Diagnostic log levels and shape

**Decision**:

- **No-match outcome (request did not resolve to any shell)**: `Information` level, single structured log entry per request, with: requested `Path` (full), `Host`, header values consulted, claim values consulted, and a bounded representation of the candidate blueprints the index considered (capped at `WebRoutingShellResolverOptions.NoMatchLogCandidateCap` — see R-007). Default cap: 10. Above the cap, the entry includes `(+N more)` rather than the full list.
- **Match outcome**: `Debug` level. Single line naming the resolved `ShellId` and the routing mode that matched.
- **Index population error** (provider throws during refresh): `Warning` level on the route index logger. The previous good snapshot remains active.
- **Index population success** (snapshot rebuilt after a lifecycle event): `Debug` level with the entry count delta.
- **Configuration error in a blueprint** (e.g., `Path` starts with `/`, duplicate routing key): `Warning` level with the blueprint name and the offending value.
- **`CShellsStartupHostedService`'s "registry remains idle" log line**: replaced with two separate lines:
  - When pre-warm list is empty: `Information` — *"CShells startup: no shells pre-warmed; routing will activate shells lazily on first request."*
  - When pre-warm list is non-empty: existing message *"CShells startup: pre-warming N shell(s)."* unchanged.

**Rationale**:
- `Information` for the no-match case: this is the failure mode the originating bug report identified as a black hole. Making it `Information` (not `Warning`) keeps unmatched-but-expected requests (e.g., probes hitting `/health` via the shell pipeline) from spamming warning channels, while still being visible in default production log configurations.
- `Debug` for matched outcomes: high-volume; only useful when actively diagnosing routing.
- The startup-line rephrasing kills the misleading "until first activation" wording that today implies the registry is only activated by `PreWarmShells` and is otherwise stuck.

**Alternatives considered**:
- **`Debug` for no-match**: rejected — guarantees the bug-report failure mode stays invisible in default production configurations.
- **`Warning` for no-match**: rejected — would fire on every legitimately-not-our-route request (e.g., a misdirected probe), drowning real warnings.
- **Structured event source**: nice but adds dependency footprint and the `ILogger<T>` structured-property mechanism is sufficient for the diagnostic need.

## R-007: New options surface

**Decision**: Add two configuration knobs to the existing `WebRoutingShellResolverOptions` (no new options class):

- `int NoMatchLogCandidateCap { get; set; } = 10;` — bound on the number of candidate blueprints serialized in the no-match log entry (R-006).
- `bool LogMatches { get; set; } = false;` — when true, emits the per-request match log entry at `Debug` level (R-006).

No options for index-population strategy: the hybrid behaviour (R-001) is a fixed implementation detail, not a knob, because the spec's success criteria require the lazy-on-first-use behaviour for non-name modes.

**Rationale**:
- Adding to the existing options class avoids introducing a parallel options object for one feature.
- The two knobs cover the spec's explicit "configurable cap" requirement (FR-008) and a long-requested diagnostic affordance.
- Resisting more knobs (e.g., "force eager population", "disable index entirely") preserves Principle VI's minimalism — those would be configurations of the wrong abstraction.

**Alternatives considered**:
- **A separate `ShellRouteIndexOptions`**: warranted only if we expected substantial growth in index-specific knobs. We don't; the index is meant to be invisible.

## R-008: Where do `WebRouting:*` values come from in the new model?

**Decision**: The route index reads `WebRouting:*` keys from `IShellBlueprint.Properties` (the per-shell `Configuration` section established in feature `007`'s `ConfigurationShellBlueprint`). `IShellRouteIndexBuilder` calls `blueprint.Properties.GetSection("WebRouting")` and reads `Path`, `Host`, `HeaderName`, `ClaimKey` as strings. Empty / null values mean the blueprint does not opt into that mode.

Concretely the existing `WebRoutingShellResolver` reads these values from the **active shell's** `ShellSettings` via `settings.GetConfiguration("WebRouting:Path")`. The route index reads them from the **blueprint's** `Properties` instead — same logical path, different source. No configuration shape change.

**Rationale**:
- Routing metadata is intrinsic to the blueprint, not derived from runtime state. Reading from `Properties` is the right place.
- This is the only point in the design that touches the blueprint's property model — confirming the assumption is the purpose of this research item.
- Once read, the values are stored in `ShellRouteEntry` records and become the source of truth for the index. The runtime `ShellSettings.GetConfiguration` access path in `WebRoutingShellResolver` is removed.

**Alternatives considered**:
- **Continue reading from `ShellSettings`**: requires the shell to be active, which is the bug. Rejected.
- **Add a dedicated `IBlueprintRoutingMetadata` projection on the blueprint**: nice for type safety but adds a contract. Deferred to a future feature if `WebRouting:*` grows beyond four string properties.

## R-009: Where does `CShellsStartupHostedService` live in the post-007 layout?

**Decision (to confirm during implementation)**: After `007`/`008`/`009` the startup hosted service should live in `CShells/Hosting/CShellsStartupHostedService.cs`. The log-line rephrasing (R-006) applies at whichever path the file currently occupies; the `plan.md` Project Structure section will be reconciled with reality when the implementation phase opens the file.

**Rationale**: The path was named in `plan.md` based on `007`'s plan; if `008` or `009` moved it, the implementation follows reality. This is a documentation issue, not a design question.
