# Implementation Plan: Scale-Ready Blueprint Provider/Manager Split

**Branch**: `007-blueprint-provider-split` | **Date**: 2026-04-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/007-blueprint-provider-split/spec.md`

## Summary

Replace the eager, imperative blueprint-registration surface introduced by feature `006`
with a scale-ready, source-agnostic model built around two small contracts:

- **`IShellBlueprintProvider`** — lazy, paginated catalogue. One `GetAsync(name)` lookup on
  the hot path; a `ListAsync(query)` method with opaque cursor pagination for admin flows.
  No eager enumeration of the catalogue at any point in normal operation.
- **`IShellBlueprintManager`** — optional write-side peer, owning a subset of names via
  `Owns(name)`. Handles `CreateAsync` / `UpdateAsync` / `DeleteAsync` against the
  underlying store. Providers whose source is read-only simply do not register a manager.

The registry becomes an index of *active generations* only — it no longer holds the
blueprint catalogue. Activation is lazy: `GetOrActivateAsync(name)` consults the provider
on first touch, then caches the live shell. Startup cost is O(pre-warmed shells), not
O(registered blueprints).

A built-in composite provider multiplexes `IEnumerable<IShellBlueprintProvider>` in DI,
preserving registration order for lookups, merging pages across sources via a composite
cursor, and detecting duplicate names lazily at first collision.

This is a **clean overhaul**. The feature-`006` registry operations
`RegisterBlueprint`, `GetBlueprint`, `GetBlueprintNames`, `ReloadAllAsync` are deleted
outright. The old eager `IShellBlueprintProvider` contract is replaced in-place. Every
downstream integration (`CShells.AspNetCore`, `CShells.FastEndpoints`,
`CShells.Providers.FluentStorage`, samples, tests) is migrated in the same PR; no
compatibility shim remains.

## Technical Context

**Language/Version**: C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Hosting.Abstractions`, `Ardalis.GuardClauses`; no new third-party packages
**Storage**: N/A — in-memory registry; providers own their own storage (code, configuration, or blob via `FluentStorage`)
**Testing**: xUnit 2.x with `Assert.*`; unit tests mirror `src/` structure; integration tests in `Integration/Lifecycle/`
**Target Platform**: .NET server / generic host
**Performance Goals**: SC-001 — startup time with 100 000 blueprints indistinguishable (within 50 ms) from startup with 10; SC-002 — activation stampede of 1 000 concurrent callers triggers exactly one provider lookup; SC-003 — 100 000 blueprints page through in at most 1 000 page requests
**Constraints**: No eager catalogue enumeration at startup or in the activation hot path (FR-003, FR-026); per-name activation serialization reuses feature `006`'s `SemaphoreSlim(1,1)` per `NameSlot`; pagination cursors are opaque base64; no HMAC signing (admin API assumed to be behind host-level authorization)
**Scale/Scope**: Up to 100 000+ blueprints in a catalogue; up to a few thousand active generations at any moment on one host (bounded by per-shell memory cost); 0-N providers composed under the built-in composite

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Abstraction-First** | ✅ PASS | Every new public contract (`IShellBlueprintProvider`, `IShellBlueprintManager`, `ProvidedBlueprint`, `BlueprintListQuery`/`Page`/`Summary`, `ShellListQuery`/`Page`/`Summary`, `ReloadOptions`, four new exceptions) lives in `CShells.Abstractions/Lifecycle/`. Implementations (`InMemoryShellBlueprintProvider`, `CompositeShellBlueprintProvider`, `ConfigurationShellBlueprintProvider`) live in `CShells/Lifecycle/Providers/`. |
| **II. Feature Modularity** | ✅ PASS | Feature model is orthogonal to blueprint sourcing; `IShellFeature.ConfigureServices` is unchanged. Blueprints still compose through the same `ShellSettings` pipeline. |
| **III. Modern C# Style** | ✅ PASS | Nullable enabled, file-scoped namespaces, primary constructors, `Guard.Against.*`, expression-bodied members, collection expressions, `internal sealed` on implementation classes. |
| **IV. Explicit Error Handling** | ✅ PASS | Four structured exceptions (`ShellBlueprintNotFoundException`, `ShellBlueprintUnavailableException`, `BlueprintNotMutableException`, `DuplicateBlueprintException`), each carrying the shell name plus contextual data (owning provider type, inner cause). Guard clauses at every public method entry. |
| **V. Test Coverage** | ✅ PASS | Unit tests for `InMemoryShellBlueprintProvider`, `CompositeShellBlueprintProvider` (lookup, list merge, cursor encoding, duplicate detection), cursor codec round-trips, each exception's message shape. Integration tests for `GetOrActivateAsync` stampede, `ReloadActiveAsync` parallelism, `UnregisterBlueprintAsync` store-first ordering, middleware translation of provider exceptions to HTTP responses. |
| **VI. Simplicity & Minimalism** | ✅ PASS | One provider contract, one manager contract, three concrete providers (in-memory, composite, configuration). Legacy eager `IShellBlueprintProvider` and `IShellRegistry` blueprint-registration operations are deleted outright rather than retained alongside. The composite's duplicate-name detection is lazy (at collision) rather than a scan-at-startup mechanism. |
| **VII. Lifecycle & Concurrency** | ✅ PASS | `GetOrActivateAsync` uses the existing per-name `SemaphoreSlim(1,1)` from the `NameSlot` introduced in feature `006` to serialize activation — no separate lock. Partial-state cleanup on activation failure uses existing CAS-based drain. `UnregisterBlueprintAsync` serializes against activation via the same per-name semaphore. Subscriber-isolation and monotonic state machine are unchanged from feature `006`. |

**Breaking changes justified**: the feature-`006` eager `IShellBlueprintProvider` and the
imperative `IShellRegistry.RegisterBlueprint`/`GetBlueprint`/`GetBlueprintNames`/`ReloadAllAsync`
surface are removed entirely. Principle VI explicitly permits breaking changes that improve
API clarity. The new surface is strictly smaller, conceptually simpler, and collapses two
concerns (catalogue sourcing vs. live registration) into one. All downstream projects are
migrated in the same PR.

## Project Structure

### Documentation (this feature)

```text
specs/007-blueprint-provider-split/
├── plan.md                                    ← this file
├── research.md                                ← Phase 0
├── data-model.md                              ← Phase 1
├── quickstart.md                              ← Phase 1
├── contracts/                                 ← Phase 1
│   ├── IShellBlueprintProvider.md
│   ├── IShellBlueprintManager.md
│   ├── IShellRegistry.md                      (delta from feature 006)
│   ├── ProvidedBlueprint.md
│   ├── Pagination.md                          (BlueprintListQuery/Page/Summary + ShellListQuery/Page/Summary + cursor codec)
│   └── Exceptions.md                          (four new exception types)
├── checklists/
│   └── requirements.md                        ← /speckit.specify output
└── tasks.md                                   ← Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/CShells.Abstractions/
└── Lifecycle/
    ├── IShellBlueprintProvider.cs             (REWRITTEN — new contract: GetAsync, ExistsAsync, ListAsync)
    ├── IShellBlueprintManager.cs              (NEW)
    ├── ProvidedBlueprint.cs                   (NEW)
    ├── BlueprintListQuery.cs                  (NEW)
    ├── BlueprintPage.cs                       (NEW)
    ├── BlueprintSummary.cs                    (NEW)
    ├── ShellListQuery.cs                      (NEW)
    ├── ShellPage.cs                           (NEW)
    ├── ShellSummary.cs                        (NEW)
    ├── ReloadOptions.cs                       (NEW)
    ├── ShellBlueprintNotFoundException.cs     (NEW)
    ├── ShellBlueprintUnavailableException.cs  (NEW)
    ├── BlueprintNotMutableException.cs        (NEW)
    ├── DuplicateBlueprintException.cs         (NEW)
    └── IShellRegistry.cs                      (MODIFIED — drop RegisterBlueprint/GetBlueprint*/ReloadAllAsync; add GetOrActivateAsync/GetBlueprintAsync/GetManager/UnregisterBlueprintAsync/ListAsync/ReloadActiveAsync)

src/CShells/
├── Lifecycle/
│   ├── ShellRegistry.cs                       (MODIFIED — internal blueprint dict removed; delegates lookup to composite provider; new methods; ReloadActiveAsync replaces ReloadAllAsync)
│   ├── Providers/                             (NEW namespace)
│   │   ├── InMemoryShellBlueprintProvider.cs  (NEW — backs AddShell(...) calls; optional manager via ctor)
│   │   ├── CompositeShellBlueprintProvider.cs (NEW — wraps IEnumerable<IShellBlueprintProvider>; lazy duplicate detection; composite cursor codec)
│   │   ├── CompositeCursorCodec.cs            (NEW — internal base64 encode/decode for multi-provider cursors)
│   │   └── ConfigurationShellBlueprintProvider.cs (NEW — replaces the 006 ConfigurationShellBlueprint with a lazy, paginated provider scanning appsettings Shells section)
│   └── Blueprints/
│       ├── DelegateShellBlueprint.cs          (kept; used by InMemoryShellBlueprintProvider to wrap AddShell delegates)
│       └── ConfigurationShellBlueprint.cs     (DELETED — functionality absorbed into ConfigurationShellBlueprintProvider)
├── Hosting/
│   └── CShellsStartupHostedService.cs         (MODIFIED — no eager provider enumeration; no eager blueprint registration; only runs pre-warm list if host configured one)
├── Configuration/
│   └── CShellsBuilderExtensions.cs            (MODIFIED — AddShell(...) routes to the in-memory provider singleton; remove any existing calls to registry.RegisterBlueprint)
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs         (MODIFIED — register InMemoryShellBlueprintProvider as singleton; register CompositeShellBlueprintProvider as the primary IShellBlueprintProvider; register IEnumerable<IShellBlueprintManager> for manager discovery)

src/CShells.AspNetCore/
├── Middleware/
│   └── ShellMiddleware.cs                     (MODIFIED — GetActive→GetOrActivateAsync; translate ShellBlueprintNotFoundException→404, ShellBlueprintUnavailableException→503)
└── Resolution/
    ├── WebRoutingShellResolver.cs             (MODIFIED — no more registry.GetBlueprintNames; route resolution consults provider via registry.GetBlueprintAsync/ListAsync as appropriate)
    └── DefaultShellResolverStrategy.cs        (MODIFIED — same)

src/CShells.Providers.FluentStorage/          (MODIFIED in place)
├── FluentStorageShellBlueprintProvider.cs    (RENAMED from FluentStorageShellSettingsProvider.cs; implements IShellBlueprintProvider + IShellBlueprintManager; no sync-over-async)
└── CShellsBuilderExtensions.cs                (MODIFIED — register the new provider; drop GetAwaiter().GetResult() pattern)

samples/
├── CShells.Workbench/                         (MODIFIED — README updated; AddShell usage unchanged; demo worker shows GetOrActivateAsync on first request)
└── CShells.Workbench.Features/                (unchanged)

tests/CShells.Tests/
├── Unit/Lifecycle/
│   ├── Providers/
│   │   ├── InMemoryShellBlueprintProviderTests.cs      (NEW)
│   │   ├── CompositeShellBlueprintProviderTests.cs     (NEW — lookup order, list merge, duplicate detection, cursor round-trip)
│   │   ├── CompositeCursorCodecTests.cs                (NEW)
│   │   └── ConfigurationShellBlueprintProviderTests.cs (NEW)
│   └── ReloadOptionsTests.cs                          (NEW — defaults + guard clauses)
└── Integration/Lifecycle/
    ├── ShellRegistryGetOrActivateTests.cs             (NEW — happy path, stampede, not-found, unavailable)
    ├── ShellRegistryUnregisterTests.cs                (NEW — manager present vs absent, store-first ordering)
    ├── ShellRegistryListTests.cs                      (NEW — pagination, filters, join with lifecycle state)
    ├── ShellRegistryReloadActiveTests.cs              (RENAMED from ShellRegistryReloadAllTests.cs; adds MaxDegreeOfParallelism coverage)
    └── AspNetCore/
        └── ShellMiddlewareLazyActivationTests.cs      (NEW — 404/503 translation; first-request activation)
```

**Structure Decision**: Everything new sits under the existing `Lifecycle/` namespace
(abstractions + implementation). A new `Providers/` sub-namespace under
`CShells/Lifecycle/` holds the three concrete providers. The single file
`ConfigurationShellBlueprint.cs` is deleted — its logic moves into
`ConfigurationShellBlueprintProvider.cs` where lazy lookup is natural. No new projects,
no new package dependencies.

## Complexity Tracking

> No constitution violations to justify. The refactor further simplifies the registry
> (removes an internal blueprint dictionary, removes four public methods, replaces one
> eager interface with a lazy one) and introduces one new contract (manager) whose
> addition is directly motivated by FR-005-FR-011 and user story 2.
