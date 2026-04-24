# Phase 0 Research: Scale-Ready Blueprint Provider/Manager Split

**Feature**: [007-blueprint-provider-split](spec.md)
**Date**: 2026-04-24

This document resolves the open design questions that Phase 1 (data model + contracts)
needs settled before it can proceed. Each section records the decision, the rationale,
and the alternatives that were considered and rejected.

## R-001: Composite cursor format

**Decision**: Opaque base64-encoded JSON document of the form
`{"v":1,"entries":[{"p":0,"c":"<sub-cursor>"}, {"p":2,"c":"<sub-cursor>"}]}` where `p` is
the provider index in DI registration order and `c` is that provider's own cursor string.
Providers whose iteration is complete are omitted from the `entries` array. Callers treat
the whole thing as an opaque `string`.

**Rationale**:
- Each sub-provider defines its own cursor format (SQL tuple, in-memory name cutoff, blob
  continuation token). The composite MUST NOT interpret them.
- JSON + base64 keeps the encoding readable during debugging but opaque on the wire.
- Including a version field (`v`) future-proofs the format against changes to how the
  composite multiplexes state.
- Only providers with remaining work appear in `entries` — the final page's `NextCursor`
  is `null`, not a JSON doc with zero entries.
- Provider identification by DI-registration index rather than type name: type names can
  collide across assemblies; index is unambiguous within one host's composite instance.

**Alternatives considered**:
- **Concatenation with a separator** (`"p0:cur|p1:cur"`): brittle under cursor values that
  contain the separator; requires escaping; harder to extend.
- **HMAC-signed cursor**: rejected per the spec (admin API is behind host authz; signing
  adds key-rotation operational burden with no new threat-model mitigation).
- **Registry-assigned stable provider IDs**: overkill; DI-registration order is stable
  for the life of a host process, which is the only window a cursor needs to be valid.

## R-002: Per-name activation serialization

**Decision**: Reuse the existing `NameSlot` construct from feature `006` (one
`SemaphoreSlim(1,1)` per shell name, lazily created via `ConcurrentDictionary.GetOrAdd`).
`GetOrActivateAsync` acquires the same slot's semaphore that `ReloadAsync` and
`ActivateAsync` use, ensuring activation and reload cannot race for the same name.

**Rationale**:
- Feature `006` already established per-name semaphore serialization for reload and
  activation (`ShellRegistry.NameSlot.Semaphore`). Adding `GetOrActivateAsync` as a third
  operation under the same slot is additive and costs nothing in complexity.
- The stampede requirement (SC-002: 1 000 concurrent callers → one provider lookup) is
  naturally satisfied by the first caller winning the semaphore, publishing the shell,
  and subsequent callers finding the now-active shell on their fast-path check before
  they ever queue on the semaphore.
- No new synchronization primitives needed.

**Alternatives considered**:
- **Per-operation lock type** (e.g., a distinct `ActivationLock` on `NameSlot`): wastes a
  lock object per name; introduces the possibility of activation racing reload for the
  same name if the locks are ever held simultaneously.
- **`Lazy<Task<IShell>>` cached on the slot**: cleaner for read-heavy workloads but
  awkward for the error path — a failed activation must not be cached, and `Lazy` gives
  no clean way to invalidate. Semaphore + fast-path check handles both success and
  failure uniformly.

## R-003: Error-path cleanup on activation failure

**Decision**: If `GetOrActivateAsync` fails during provider lookup, shell build, or
initializer execution, the `NameSlot.ActiveShell` field MUST be left `null` and any
partially-constructed shell MUST be disposed before the exception propagates. Subsequent
calls retry the lookup from scratch. This mirrors feature `006`'s `ActivateAsync` partial-
failure semantics.

**Rationale**:
- FR-017 requires a retryable error (`ShellBlueprintUnavailableException`) — caching the
  failure would break that contract.
- Feature `006` already established the "no partial state" invariant; extending it to
  `GetOrActivateAsync` keeps the registry's state-machine guarantees uniform across
  activation entry points.
- Partial shells are disposed via the existing `DisposePartialProviderAsync` helper
  (already extracted in feature `006`'s `ShellRegistry`).

**Alternatives considered**:
- **Cache the failed lookup for a short TTL**: tempting for DB-outage scenarios (avoid
  hammering the provider during an outage), but introduces an observability black hole
  (callers can't distinguish "never tried" from "failed 30s ago"). Circuit-breaking is a
  cross-cutting concern that belongs in the provider implementation (e.g., Polly), not
  the registry.

## R-004: Manager discovery via composite provider

**Decision**: The `ProvidedBlueprint` record pairs a blueprint with its owning manager
(or `null`). Providers that wrap a mutable source construct `ProvidedBlueprint` with
their `IShellBlueprintManager` reference attached. `IShellRegistry.GetManager(name)`
performs a lookup via the composite provider and returns
`providedBlueprint?.Manager`. Concurrently-registered `IShellBlueprintManager` instances
in DI are NOT used for discovery — the provider is the sole source of manager
association.

**Rationale**:
- Pairing the manager with the blueprint at its source eliminates the "which manager
  owns this name?" question — the provider that vended the blueprint owns the answer.
- A single class can implement both interfaces (FluentStorage will), or they can be
  separate; either works.
- Avoids an `IEnumerable<IShellBlueprintManager>` + `Owns(name)` probe loop at every
  `GetManager` call (though `Owns` is still exposed on the manager interface for
  ergonomics when callers hold a manager directly).

**Alternatives considered**:
- **Separate `IEnumerable<IShellBlueprintManager>` scan** per lookup: wastes cycles and
  introduces a consistency question (what if the blueprint comes from provider A but a
  manager for the same name is registered by provider B?). Tying the manager to the
  provider's vended blueprint makes the association unambiguous.

## R-005: Composite duplicate-name detection

**Decision**: Detect duplicates lazily at two points:
1. **During `GetBlueprintAsync(name)`**: after the first non-null hit, continue probing
   subsequent providers *only if* a runtime configuration flag
   `CompositeProviderOptions.DetectDuplicatesOnLookup` is set (default: true in Debug,
   false in Release). Duplicates found raise `DuplicateBlueprintException`.
2. **During `ListAsync`**: the composite's list-merge phase always de-duplicates against
   already-yielded names within a page; an intra-page duplicate raises immediately. A
   cross-page duplicate (page 3 yields `X`, page 5 yields `X` again from a different
   provider) is detected by the composite maintaining a rolling Bloom filter across
   pages; false positives re-check via `ExistsAsync` on earlier providers.

**Rationale**:
- The spec's FR-014 allows lazy detection; scanning every provider for every lookup
  doubles the hot-path cost for the sake of catching a configuration error.
- Release-build default of "short-circuit on first hit" gives the expected production
  performance profile; Debug default of "detect duplicates" catches the error during
  development.
- `ListAsync`-path detection is always-on because listing is an admin flow, not a hot
  path, and the cost of a rolling Bloom filter is negligible compared to an I/O-bound
  database page.

**Alternatives considered**:
- **Always detect on lookup**: rejected per above — not free, not required by the spec,
  and the listing-path check is sufficient for most operator-observable scenarios.
- **Detect at composite construction time**: rejected — would require enumerating every
  provider's full catalogue at startup, which is the anti-goal of this entire feature.

## R-006: Pre-warming of shells at startup

**Decision**: The `CShellsStartupHostedService` no longer enumerates the catalogue or
registers blueprints at startup. A new optional builder step
`CShellsBuilder.PreWarmShells(params string[] names)` records a list of names to activate
in `StartAsync`. If no pre-warm list is configured, startup completes in constant time
regardless of catalogue size (SC-001).

**Rationale**:
- The whole point of 007 is to avoid catalogue enumeration at startup; a no-op default
  behavior is aligned with that.
- Pre-warming is a legitimate operator knob (e.g., for shells that always need to be
  hot, like a "platform" shell). Making it opt-in keeps the common case fast.
- Pre-warm failures are logged but do not abort host startup — this lets transient DB
  issues at startup not prevent the host from serving requests that target other
  shells.

**Alternatives considered**:
- **Keep eager activation for all blueprints**: violates SC-001 directly.
- **Pre-warm the top-N most-recently-used shells based on persisted state**: premature;
  defer to a future feature if host developers ask for it. Explicit list is simpler.

## R-007: ReloadActiveAsync parallelism default

**Decision**: `ReloadOptions.MaxDegreeOfParallelism` defaults to `8`. Callers can
override via the `ReloadOptions` parameter. Implementation uses `Parallel.ForEachAsync`
with the configured degree; each reload's exceptions are captured into the
`ReloadResult` list rather than aborting the batch.

**Rationale**:
- Feature `006`'s `ReloadAllAsync` had no cap, which is fine for the imperative-
  registration model where the active set was typically small. With lazy activation the
  active set can reach thousands; unbounded parallelism would thrash the thread pool
  and allocate thousands of new `ServiceProvider` roots simultaneously.
- `8` is a reasonable default: high enough to exploit multi-core hosts, low enough to
  keep memory pressure bounded during a reload storm.
- `Parallel.ForEachAsync` (added in .NET 6) provides the natural primitive —
  per-iteration async, built-in cancellation.

**Alternatives considered**:
- **`Environment.ProcessorCount` default**: machine-dependent; makes reload timing
  harder to reason about across environments.
- **Unbounded default**: re-introduces the scalability problem.
- **`SemaphoreSlim`-gated `Task.WhenAll`**: works but `Parallel.ForEachAsync` is
  idiomatic in .NET 6+ and shorter.

## R-008: In-memory provider iteration order for pagination

**Decision**: `InMemoryShellBlueprintProvider.ListAsync` returns entries in
case-insensitive ordinal order by name. The cursor for an in-memory page is the name of
the last-returned entry (`last-name`); the next page starts at the first name strictly
greater than `last-name` under the same ordinal comparison.

**Rationale**:
- Deterministic ordering is a prerequisite for stable pagination (SC-003: "pages are
  returned in a stable order across repeated runs when the catalogue is unchanged").
- Case-insensitive ordinal matches the name equality semantics used elsewhere in
  CShells (e.g., the registry's `NameSlot` dictionary).
- Encoding the last-returned name as the cursor is simpler than integer indices and
  survives concurrent mutations gracefully (a name added between pages either sorts
  before `last-name` — silently skipped, acceptable per FR on concurrent mutation — or
  after it and gets picked up on the next page).

**Alternatives considered**:
- **Integer index cursor**: breaks the moment a name is added or removed, because
  indices shift.
- **Case-sensitive ordering**: inconsistent with the rest of CShells name handling.

## Summary

All open design questions are resolved without any `NEEDS CLARIFICATION` markers
remaining. Phase 1 (data model + contracts) can proceed.
