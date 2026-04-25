---
description: "Task list for Scale-Ready Blueprint Provider/Manager Split — clean overhaul"
---

# Tasks: Scale-Ready Blueprint Provider/Manager Split

**Input**: Design documents from `/specs/007-blueprint-provider-split/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Scope**: Clean overhaul of the blueprint-registration surface introduced in feature `006`.
The registry's `RegisterBlueprint` / `GetBlueprint` / `GetBlueprintNames` / `ReloadAllAsync`
operations are removed; blueprints are owned by providers; activation is lazy. Every
downstream consumer (`CShells.AspNetCore`, `CShells.Providers.FluentStorage`, samples, tests)
is migrated in the same PR.

**Tests**: Included (Constitution Principle V). Each user story phase includes unit and/or
integration tests written before implementation. The existing feature-`006` test suite MUST
pass unchanged after migration (SC-006).

**Organization**: Phases 1–2 scaffold types. Phases 3–7 implement user stories in priority
order. Phase 8 migrates existing providers. Phase 9 deletes legacy code. Phase 10 polishes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story this task belongs to (US1…US5). Tasks not tied to a story have no label.
- File paths are repo-root relative.

## Path Conventions

- **Abstractions**: `src/CShells.Abstractions/Lifecycle/`
- **Implementation**: `src/CShells/Lifecycle/`, `src/CShells/Lifecycle/Providers/`
- **Tests**: `tests/CShells.Tests/Unit/Lifecycle/`, `tests/CShells.Tests/Integration/Lifecycle/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create directory scaffolding for the new provider/manager surface.

- [X] T001 Create directory `src/CShells/Lifecycle/Providers/`
- [X] T002 [P] Create directory `tests/CShells.Tests/Unit/Lifecycle/Providers/`

---

## Phase 2: Foundational Abstractions (Blocking Prerequisites)

**Purpose**: Create every new public type in `CShells.Abstractions`. These MUST compile
cleanly before any implementation or test work begins. All tasks in this phase are parallel —
one file each.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

### Paginated DTOs

- [X] T003 [P] Create `ProvidedBlueprint` record (`Blueprint`, `Manager`) in `src/CShells.Abstractions/Lifecycle/ProvidedBlueprint.cs` (data-model §1.1)
- [X] T004 [P] Create `BlueprintListQuery` record (`Cursor`, `Limit = 50`, `NamePrefix`) with Guard for `Limit` range `[1,500]` in `src/CShells.Abstractions/Lifecycle/BlueprintListQuery.cs` (data-model §1.2)
- [X] T005 [P] Create `BlueprintPage` record (`Items`, `NextCursor`) in `src/CShells.Abstractions/Lifecycle/BlueprintPage.cs` (data-model §1.3)
- [X] T006 [P] Create `BlueprintSummary` record (`Name`, `SourceId`, `Mutable`, `Metadata`) in `src/CShells.Abstractions/Lifecycle/BlueprintSummary.cs` (data-model §1.4)
- [X] T007 [P] Create `ShellListQuery` record (`Cursor`, `Limit = 50`, `NamePrefix`, `StateFilter`) with Guard for `Limit` range in `src/CShells.Abstractions/Lifecycle/ShellListQuery.cs` (data-model §1.5)
- [X] T008 [P] Create `ShellPage` record (`Items`, `NextCursor`) in `src/CShells.Abstractions/Lifecycle/ShellPage.cs` (data-model §1.6)
- [X] T009 [P] Create `ShellSummary` record (`Name`, `SourceId`, `Mutable`, `ActiveGeneration`, `State`, `ActiveScopeCount`, `LastScopeOpenedAt`, `Metadata`) with invariant that lifecycle fields are null iff `ActiveGeneration` is null in `src/CShells.Abstractions/Lifecycle/ShellSummary.cs` (data-model §1.7)
- [X] T010 [P] Create `ReloadOptions` record (`MaxDegreeOfParallelism = 8`) with Guard range `[1,64]` in `src/CShells.Abstractions/Lifecycle/ReloadOptions.cs` (data-model §1.8)

### Exceptions

- [X] T011 [P] Create `ShellBlueprintNotFoundException : InvalidOperationException` with `Name` property in `src/CShells.Abstractions/Lifecycle/ShellBlueprintNotFoundException.cs` (contracts/Exceptions.md)
- [X] T012 [P] Create `ShellBlueprintUnavailableException : InvalidOperationException` wrapping inner exception, with `Name` property in `src/CShells.Abstractions/Lifecycle/ShellBlueprintUnavailableException.cs` (contracts/Exceptions.md)
- [X] T013 [P] Create `BlueprintNotMutableException : InvalidOperationException` with `Name` + optional `SourceId` in `src/CShells.Abstractions/Lifecycle/BlueprintNotMutableException.cs` (contracts/Exceptions.md)
- [X] T014 [P] Create `DuplicateBlueprintException : InvalidOperationException` with `Name`, `FirstProviderType`, `SecondProviderType` in `src/CShells.Abstractions/Lifecycle/DuplicateBlueprintException.cs` (contracts/Exceptions.md)

### Interfaces

- [X] T015 [P] **Rewrite** `IShellBlueprintProvider` in `src/CShells.Abstractions/Lifecycle/IShellBlueprintProvider.cs` — replace eager `GetBlueprintsAsync` with `GetAsync`, default-impl `ExistsAsync`, and `ListAsync`. Full XML docs per contracts/IShellBlueprintProvider.md.
- [X] T016 [P] Create `IShellBlueprintManager` interface (`Owns`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`) in `src/CShells.Abstractions/Lifecycle/IShellBlueprintManager.cs` (contracts/IShellBlueprintManager.md)
- [X] T017 **Modify** `IShellRegistry` in `src/CShells.Abstractions/Lifecycle/IShellRegistry.cs`: remove `RegisterBlueprint`, `GetBlueprint`, `GetBlueprintNames`, `ReloadAllAsync`; add `GetOrActivateAsync`, `GetBlueprintAsync`, `GetManagerAsync`, `UnregisterBlueprintAsync`, `ListAsync`, `ReloadActiveAsync`. Full XML docs per contracts/IShellRegistry.md. Depends on T003–T016.

### Verification

- [X] T018 Run `dotnet build src/CShells.Abstractions/CShells.Abstractions.csproj` and confirm zero errors / zero warnings across `net8.0`, `net9.0`, `net10.0` targets. Depends on T003–T017.

**Checkpoint**: Abstractions compile. `src/CShells/` does not compile yet (registry impl stale). User stories proceed in priority order.

---

## Phase 3: User Story 1 — Scale-Ready Lazy Activation (Priority: P1) 🎯 MVP

**Goal**: Host startup is O(pre-warmed set), not O(catalogue). `GetOrActivateAsync(name)` consults the composite provider on first touch, builds generation 1, and serves subsequent callers from the in-memory index. Stampede-safe.

**Independent Test**: Register a `StubShellBlueprintProvider` with 100 000 claimed names, start the host, assert no provider enumeration occurs; issue one `GetOrActivateAsync("acme-42")` call, assert exactly one provider lookup and one shell build; issue 100 concurrent calls for `"acme-43"` from a cold state, assert exactly one lookup and one build.

### Test doubles and unit tests for US1

- [X] T019 [P] [US1] Create `StubShellBlueprintProvider` test helper (in-memory `Dictionary<string, IShellBlueprint>`, ordered by name, configurable throw behavior, lookup counter, listing counter) in `tests/CShells.Tests/TestHelpers/StubShellBlueprintProvider.cs` — used by every US1/US3/US4/US5 integration test
- [X] T020 [P] [US1] Unit tests for `InMemoryShellBlueprintProvider` (add + lookup, add + list, case-insensitive lookup, unknown name returns null) in `tests/CShells.Tests/Unit/Lifecycle/Providers/InMemoryShellBlueprintProviderTests.cs`
- [X] T021 [P] [US1] Unit tests for `ReloadOptions` guards (min 1, max 64, default 8) in `tests/CShells.Tests/Unit/Lifecycle/ReloadOptionsTests.cs`

### Core implementations for US1

- [X] T022 [US1] Implement `InMemoryShellBlueprintProvider` in `src/CShells/Lifecycle/Providers/InMemoryShellBlueprintProvider.cs`: thread-safe `ConcurrentDictionary<string, ProvidedBlueprint>`, case-insensitive keys, `GetAsync` O(1), `ListAsync` sorted by name using `last-name` cursor (research R-008), accepts optional `IShellBlueprintManager` via `Add(name, blueprint, manager)` method. Depends on T020.
- [X] T023 [US1] Implement `CompositeShellBlueprintProvider` skeleton in `src/CShells/Lifecycle/Providers/CompositeShellBlueprintProvider.cs`: wraps `IReadOnlyList<IShellBlueprintProvider>`, `GetAsync` probes in order returning first non-null, `ListAsync` merges via composite cursor (codec comes in US3 — use a placeholder that concatenates on a separator until T040 replaces it), `ExistsAsync` short-circuits at first true. No duplicate detection yet (deferred to US3).
- [X] T024 [US1] **Rewrite** `ShellRegistry.cs` at `src/CShells/Lifecycle/ShellRegistry.cs`:
  - Remove `NameSlot.Blueprint` field
  - Remove `RegisterBlueprint`, `GetBlueprint`, `GetBlueprintNames`
  - Replace internal `_blueprints` dict with an injected `IShellBlueprintProvider` (the composite)
  - Implement `GetOrActivateAsync(name)`: fast-path active check → per-name semaphore → re-check active → `provider.GetAsync(name)` → wrap exceptions (FR-017) → build shell via existing `_providerBuilder` → CAS to Active → publish. On any failure: dispose partial, leave `ActiveShell = null`, release semaphore.
  - Implement `GetBlueprintAsync(name)`: delegates to `provider.GetAsync`, returns `null` on miss, propagates exception raw.
  - Modify `ActivateAsync` and `ReloadAsync` to consult the composite provider instead of the removed `_blueprints` dict.
  - Replace `ReloadAllAsync` with `ReloadActiveAsync(ReloadOptions?)` using `Parallel.ForEachAsync(MaxDegreeOfParallelism)` (research R-007).
  - `ListAsync`, `GetManagerAsync`, `UnregisterBlueprintAsync` left as `throw new NotImplementedException()` for now — filled in by US2/US5.
  Depends on T017, T022, T023.
- [X] T025 [US1] Modify `CShellsStartupHostedService` at `src/CShells/Hosting/CShellsStartupHostedService.cs`: remove eager enumeration of `IShellBlueprintProvider.GetBlueprintsAsync`; remove the auto-activate-all-known-names loop; keep the shutdown-drain path unchanged. Add `PreWarmNames` list consumed in `StartAsync` that activates specified names (logging and continuing on individual failures per research R-006). Depends on T024.
- [X] T026 [US1] Modify `CShellsBuilderExtensions.cs` at `src/CShells/Configuration/CShellsBuilderExtensions.cs`: `AddShell(name, configure)` now captures the delegate into the singleton `InMemoryShellBlueprintProvider` via a builder-held reference, instead of constructing a blueprint and calling a removed registry method. Add `PreWarmShells(params string[] names)` extension that appends names to the hosted service's pre-warm list. Depends on T022, T025.
- [X] T027 [US1] Modify `ServiceCollectionExtensions.cs` at `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs`: register `InMemoryShellBlueprintProvider` as a singleton concrete type AND as an `IShellBlueprintProvider`; register `CompositeShellBlueprintProvider` as the primary `IShellBlueprintProvider` that the registry consumes (via keyed registration OR a dedicated `ICompositeShellBlueprintProvider` wrapper interface — implementer's choice); register the pre-warm list holder. Depends on T022, T023, T026.
- [X] T028 [US1] Verify `dotnet build src/CShells/CShells.csproj` completes cleanly (treat warnings as errors off the gate — polish phase re-enables). Depends on T024–T027.

### Integration tests for US1

- [X] T029 [P] [US1] Integration test `GetOrActivate_WithNeverActivatedName_CallsProviderOnce_AndActivatesGeneration1` in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryGetOrActivateTests.cs`
- [X] T030 [P] [US1] Integration test `GetOrActivate_WithAlreadyActiveShell_DoesNotRequeryProvider` in same file
- [X] T031 [P] [US1] Integration test `GetOrActivate_ConcurrentCallersFor1000InactiveName_TriggerExactlyOneProviderLookup_AndReturnSameShell` in same file (stampede — acceptance scenario 1.4, SC-002)
- [X] T032 [P] [US1] Integration test `GetOrActivate_WhenProviderReturnsNull_ThrowsShellBlueprintNotFoundException` in same file
- [X] T033 [P] [US1] Integration test `GetOrActivate_WhenProviderThrows_ThrowsShellBlueprintUnavailableException_WithInnerCause_AndLeavesNoPartialState` in same file
- [X] T034 [P] [US1] Integration test `PreWarmShells_ActivatesSpecifiedNamesAtStartup_AndContinuesOnIndividualFailure` in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryPreWarmTests.cs`
- [X] T035 [P] [US1] Integration test `StartUp_With100000ClaimedBlueprints_DoesNotEnumerateCatalogue` in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryLazyStartupTests.cs` (SC-001)

**Checkpoint**: MVP complete. Lazy activation works end-to-end against the composite; US1 test file is green; feature-`006` tests that touched removed members FAIL — they get fixed in later phases via user-story tasks or cleanup (Phase 9).

---

## Phase 4: User Story 2 — Mutable Sources via Manager (Priority: P1)

**Goal**: Providers whose source is mutable (e.g., blob store) implement `IShellBlueprintManager` alongside `IShellBlueprintProvider`. The registry routes writes to the owning manager via `GetManagerAsync`. `UnregisterBlueprintAsync` runs persistence-first, drain-second.

**Independent Test**: Register a stub composite with a read-only provider for name `readonly-x` and a read/write provider (same type implements both interfaces) for name `mutable-y`. Assert `GetManagerAsync("readonly-x") == null`, `GetManagerAsync("mutable-y") != null`, `UnregisterBlueprintAsync("readonly-x")` throws `BlueprintNotMutableException`, and `UnregisterBlueprintAsync("mutable-y")` invokes the manager's `DeleteAsync` exactly once BEFORE draining the active generation.

### Test doubles and unit tests for US2

- [X] T036 [P] [US2] Extend `StubShellBlueprintProvider` with a `WithManager(IShellBlueprintManager)` helper and a `StubShellBlueprintManager` double (configurable throw-on-delete, operation log) in `tests/CShells.Tests/TestHelpers/StubShellBlueprintProvider.cs`
- [X] T037 [P] [US2] Unit tests for `BlueprintNotMutableException` message shape (with and without `SourceId`) in `tests/CShells.Tests/Unit/Lifecycle/ExceptionMessageTests.cs`

### Core implementations for US2

- [X] T038 [US2] Implement `ShellRegistry.GetManagerAsync(name)` at `src/CShells/Lifecycle/ShellRegistry.cs`: delegates to `provider.GetAsync(name)`, returns `ProvidedBlueprint?.Manager`. Depends on T024.
- [X] T039 [US2] Implement `ShellRegistry.UnregisterBlueprintAsync(name)` at `src/CShells/Lifecycle/ShellRegistry.cs`:
  1. `provider.GetAsync(name)` to discover the manager; if the provider returns null, throw `ShellBlueprintNotFoundException`.
  2. If no manager, throw `BlueprintNotMutableException` with `SourceId` if the summary's `SourceId` is available.
  3. `await manager.DeleteAsync(name)` — persistence-first.
  4. Acquire the name's `NameSlot.Semaphore`.
  5. If `ActiveShell` is non-null: drain via existing `DrainAsync` machinery until `Disposed`.
  6. Remove the `NameSlot` from the registry's internal dict.
  7. Release the semaphore.
  If step 5 or 6 raises, persist-delete has already succeeded — rethrow but DO NOT attempt to restore persistent state.
  Depends on T038.

### Integration tests for US2

- [X] T040 [P] [US2] Integration test `GetManager_ForReadOnlyOwnedName_ReturnsNull` in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryUnregisterTests.cs`
- [X] T041 [P] [US2] Integration test `GetManager_ForMutableOwnedName_ReturnsOwningManager` in same file
- [X] T042 [P] [US2] Integration test `UnregisterBlueprint_CallsManagerDeleteAsyncFirst_ThenDrainsActiveGeneration_InOrder` in same file (acceptance scenario 2.3 — SC-005)
- [X] T043 [P] [US2] Integration test `UnregisterBlueprint_WithNoManager_ThrowsBlueprintNotMutable_AndLeavesRuntimeStateUntouched` in same file (acceptance scenario 2.4)
- [X] T044 [P] [US2] Integration test `UnregisterBlueprint_WhenManagerDeleteFails_Propagates_AndLeavesActiveGenerationServingRequests` in same file (acceptance scenario 2.5)
- [X] T045 [P] [US2] Integration test `UnregisterBlueprint_ThenGetOrActivate_ForSameName_ThrowsShellBlueprintNotFoundException` in same file (SC-005)
- [X] T046 [P] [US2] Integration test `Create_ViaManager_ThenGetOrActivate_ForThatName_ActivatesGeneration1` in same file (acceptance scenario 2.6)

**Checkpoint**: Manager write-side is fully routed. Read-only sources cleanly reject mutations.

---

## Phase 5: User Story 3 — Composite Deterministic Precedence (Priority: P2)

**Goal**: Multiple providers compose into a single catalogue view. Lookups probe in DI-registration order; duplicate names are detected lazily and raised with structured context. Paging emits a composite cursor that round-trips across sub-providers.

**Independent Test**: Register three stub providers (A, B, C) with disjoint names; assert `GetBlueprintAsync` for any name probes the owning provider and short-circuits further probes. Register two stub providers both claiming `"conflict"`; assert `GetBlueprintAsync("conflict")` throws `DuplicateBlueprintException` naming both provider types. Register two providers each with 10 blueprints, paginate `ListAsync` with `Limit=5`, assert exactly 4 pages returned with 20 unique names total in deterministic order.

### Cursor codec

- [X] T047 [P] [US3] Unit tests for `CompositeCursorCodec` in `tests/CShells.Tests/Unit/Lifecycle/Providers/CompositeCursorCodecTests.cs`: round-trip 0/1/2/3 sub-entries, unknown version rejection, corrupted base64 rejection, empty-entries normalization, null round-trip
- [X] T048 [US3] Implement `CompositeCursorCodec` (internal static class) in `src/CShells/Lifecycle/Providers/CompositeCursorCodec.cs`: base64-JSON encode/decode per contracts/Pagination.md. Depends on T047.
- [X] T049 [US3] Update `CompositeShellBlueprintProvider.ListAsync` at `src/CShells/Lifecycle/Providers/CompositeShellBlueprintProvider.cs` to use `CompositeCursorCodec` instead of the placeholder from T023. Depends on T048.

### Duplicate detection

- [X] T050 [US3] Implement duplicate detection in `CompositeShellBlueprintProvider.GetAsync`: when `CompositeProviderOptions.DetectDuplicatesOnLookup == true` (default: Debug builds yes, Release builds no per research R-005), after first non-null hit continue probing remaining providers; on second hit raise `DuplicateBlueprintException`.
- [X] T051 [US3] Implement duplicate detection in `CompositeShellBlueprintProvider.ListAsync`: maintain a per-call `HashSet<string>` of names already yielded in this listing session (caller-supplied via cursor state) OR use a case-insensitive rolling `HashSet` across the merge; a cross-sub-provider name collision raises `DuplicateBlueprintException` immediately.
- [X] T052 [US3] Add `CompositeProviderOptions` class in `src/CShells/Lifecycle/Providers/CompositeProviderOptions.cs` (flag: `DetectDuplicatesOnLookup`). Register as a configurable options object in DI.

### Integration tests for US3

- [X] T053 [P] [US3] Integration test `GetBlueprint_WithDisjointNamesAcrossProviders_RoutesToOwningProvider_AndShortCircuits` in `tests/CShells.Tests/Integration/Lifecycle/CompositeShellBlueprintProviderTests.cs`
- [X] T054 [P] [US3] Integration test `GetBlueprint_WithDuplicateNameAcrossProviders_ThrowsDuplicateBlueprint_NamingBothProviderTypes` in same file (acceptance scenario 3.2)
- [X] T055 [P] [US3] Integration test `ListAsync_With1000BlueprintsAcrossTwoProviders_YieldsEachNameExactlyOnce_AcrossPages` in same file (acceptance scenario 3.3)
- [X] T056 [P] [US3] Integration test `ListAsync_WithNextCursor_ResumesIterationWithoutDuplicatesOrGaps` in same file (acceptance scenario 5.2)
- [X] T057 [P] [US3] Integration test `ListAsync_CrossPageDuplicate_IsDetected_AndThrows` in same file

**Checkpoint**: Composite is production-grade: ordered lookup + duplicate detection + stable pagination.

---

## Phase 6: User Story 4 — ASP.NET Core Middleware Lazy Activation (Priority: P2)

**Goal**: First-request activation is wired end-to-end through the routing middleware. Missing blueprints → 404. Unavailable providers → 503. Concurrent requests for the same cold shell serialize on activation.

**Independent Test**: Spin a minimal ASP.NET Core TestHost with `StubShellBlueprintProvider` containing name `tenant-x`; issue an HTTP GET for a route that resolves to `tenant-x`, assert 200 and the shell is now active. Issue a GET that resolves to an unknown name, assert 404. Configure the stub to throw on `GetAsync`, issue a GET, assert 503.

### ASP.NET Core middleware updates

- [X] T058 [US4] Modify `ShellMiddleware` at `src/CShells.AspNetCore/Middleware/ShellMiddleware.cs`: replace `registry.GetActive(name)` with `await registry.GetOrActivateAsync(name)`; catch `ShellBlueprintNotFoundException` → `context.Response.StatusCode = 404; return;`; catch `ShellBlueprintUnavailableException` → `context.Response.StatusCode = 503; return;`. Leave other exceptions unhandled (they propagate to the host's error handler). Depends on T024.
- [X] T059 [US4] Modify `WebRoutingShellResolver` at `src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs`: remove the feature-`006` call to `registry.GetBlueprintNames()`; if route resolution needs to probe name existence, use `registry.GetBlueprintAsync(name)` or accept that routing is inherently lazy (unknown names fall through to 404). Document the chosen approach in XML docs.
- [X] T060 [US4] Modify `DefaultShellResolverStrategy` at `src/CShells/Resolution/DefaultShellResolverStrategy.cs`: same treatment as T059 — no `GetBlueprintNames` reliance.
- [X] T061 [US4] Verify `dotnet build src/CShells.AspNetCore/CShells.AspNetCore.csproj` completes cleanly. Depends on T058–T060.

### Integration tests for US4

- [X] T062 [P] [US4] Integration test `Request_ForNeverActivatedShell_TriggersGetOrActivate_AndIsServed` in `tests/CShells.Tests/Integration/AspNetCore/ShellMiddlewareLazyActivationTests.cs` (acceptance scenario 4.1)
- [X] T063 [P] [US4] Integration test `Request_ForUnknownShellName_Returns404` in same file (acceptance scenario 4.2)
- [X] T064 [P] [US4] Integration test `Request_WhenProviderThrowsUnavailable_Returns503_AndSubsequentRequestRetries` in same file (acceptance scenario 4.3)
- [X] T065 [P] [US4] Integration test `Request_ConcurrentFor1000ColdShell_SerializeOnActivation_OneLookupOneBuild_ManySuccessfulResponses` in same file

**Checkpoint**: Real HTTP flow end-to-end lazy. First request for a brand-new shell activates and serves.

---

## Phase 7: User Story 5 — Paginated Catalogue Listing (Priority: P3)

**Goal**: `IShellRegistry.ListAsync` exposes a paginated, filterable, lifecycle-aware view of the full catalogue. The registry left-joins the composite provider's pages with its in-memory active-shell state. No existing consumer ships in this feature (the admin API lands in 009); locking in the contract now avoids a breaking redesign later.

**Independent Test**: Register 1 000 stubs across two providers (500 each); call `registry.ListAsync(new ShellListQuery(Limit: 100))`, assert exactly 10 pages of 100 each, every name exactly once. Activate one shell from each provider, re-list, assert `ActiveGeneration` and `State` populated on exactly those two entries.

### Registry list implementation

- [X] T066 [US5] Implement `ShellRegistry.ListAsync(ShellListQuery)` at `src/CShells/Lifecycle/ShellRegistry.cs`:
  - Delegate to `provider.ListAsync(new BlueprintListQuery(...))` for catalogue enumeration.
  - For each `BlueprintSummary` in the returned page, look up `NameSlot[name]` in the in-memory registry dict. If present and `ActiveShell != null`, populate `ActiveGeneration`, `State`, `ActiveScopeCount`, `LastScopeOpenedAt` from the live shell; otherwise leave as null.
  - If `query.StateFilter` is non-null, filter entries down to those with an active shell in the requested state before returning.
  - Preserve the provider's `NextCursor` verbatim.
  Depends on T024.

### Integration tests for US5

- [X] T067 [P] [US5] Integration test `ListAsync_With1000BlueprintsAcrossTwoProviders_PagesOf100_Yields10Pages_EachNameOnce` in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryListTests.cs` (acceptance scenario 5.1, SC-003)
- [X] T068 [P] [US5] Integration test `ListAsync_WithNamePrefix_OnlyReturnsMatchingEntries` in same file (acceptance scenario 5.3)
- [X] T069 [P] [US5] Integration test `ListAsync_LeftJoinsLifecycleState_ActiveShellsCarryGenerationAndState_InactiveShellsHaveNullFields` in same file
- [X] T070 [P] [US5] Integration test `ListAsync_WithStateFilter_FiltersOutInactiveBlueprintsAndMismatchedStates` in same file

**Checkpoint**: Contract locked. Feature `009` admin API can build atop `registry.ListAsync` without requiring further changes here.

---

## Phase 8: Migration of Existing Providers (cross-cutting)

**Purpose**: Bring `ConfigurationShellBlueprintProvider` and `FluentStorageShellBlueprintProvider` onto the new contract. This phase touches existing consumers and tests.

### Configuration provider

- [X] T071 Create `ConfigurationShellBlueprintProvider` at `src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs` implementing the new `IShellBlueprintProvider`. Backed by an `IConfiguration` section ("Shells" by default); `GetAsync(name)` reads the sub-section lazily; `ListAsync` enumerates the keys under the section in case-insensitive order with a last-key cursor. No manager (read-only source). The per-shell `IShellBlueprint` returned wraps the sub-section via the existing composition logic (moved here from the deleted `ConfigurationShellBlueprint` — see T081).
- [X] T072 Modify `CShellsBuilderExtensions.AddShellsFromConfiguration(section)` at `src/CShells/Configuration/CShellsBuilderExtensions.cs`: registers `ConfigurationShellBlueprintProvider` as an `IShellBlueprintProvider` singleton with the supplied section. Depends on T071.
- [X] T073 [P] Unit tests for `ConfigurationShellBlueprintProvider` in `tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs`: GetAsync hit/miss, ListAsync ordering + cursor round-trip, feature-section composition.

### FluentStorage provider

- [X] T074 Rename `src/CShells.Providers.FluentStorage/FluentStorageShellSettingsProvider.cs` to `FluentStorageShellBlueprintProvider.cs`. Replace its content with an implementation of BOTH `IShellBlueprintProvider` AND `IShellBlueprintManager`: on-demand async blob reads (`GetAsync`), blob-listing with continuation-token cursor (`ListAsync`), prefix-based `Owns(name)`, `CreateAsync`/`UpdateAsync`/`DeleteAsync` against the blob container. No `GetAwaiter().GetResult()` anywhere (SC-007).
- [X] T075 Modify `src/CShells.Providers.FluentStorage/CShellsBuilderExtensions.cs`: `AddFluentStorageBlueprints(opts)` registers the single concrete provider as both `IShellBlueprintProvider` and `IShellBlueprintManager` via DI. Depends on T074.
- [X] T076 [P] Unit tests for `FluentStorageShellBlueprintProvider` in `tests/CShells.Tests/Unit/Providers/FluentStorageShellBlueprintProviderTests.cs` using `StorageFactory.Blobs.InMemory()`: create/read/update/delete round-trip, listing with continuation, concurrent create uniqueness enforcement.

### Samples

- [X] T077 Update `samples/CShells.Workbench/README.md` to show the lazy-activation + pre-warm patterns from `quickstart.md`. Explain that `AddShell(...)` is unchanged.
- [X] T078 Update `samples/CShells.Workbench` worker/demo code: adjust any code that called removed registry members (`GetBlueprintNames`, `ReloadAllAsync`) to use the new surface. Where a demo was iterating all known names, switch to `ListAsync` paging.

---

## Phase 9: Cleanup — Delete Legacy Surface

**Purpose**: Remove feature-`006` code that has been replaced. Must leave zero references behind.

- [X] T079 [P] Delete `src/CShells/Lifecycle/Blueprints/ConfigurationShellBlueprint.cs` — its logic moved into `ConfigurationShellBlueprintProvider` in T071.
- [X] T080 [P] Delete any feature-`006` `IShellBlueprintProvider` call sites that used the old eager `GetBlueprintsAsync` signature — the interface was rewritten in T015, and the hosted service no longer enumerates (T025).
- [X] T081 [P] Delete or rename obsolete tests that referenced removed registry members (`RegisterBlueprint`, `GetBlueprint`, `GetBlueprintNames`, `ReloadAllAsync`, `ConfigurationShellBlueprint`). Identify via `grep -r` across `tests/`. Integration tests that covered `ReloadAllAsync` merge into `ShellRegistryReloadActiveTests` (T082).
- [X] T082 Rename `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryReloadAllTests.cs` → `ShellRegistryReloadActiveTests.cs`. Update every test to use `ReloadActiveAsync(new ReloadOptions(...))`. Add a new test `ReloadActive_With50Actives_RespectsMaxDegreeOfParallelism`.
- [X] T083 [P] Delete test doubles that implemented the old eager `IShellBlueprintProvider` surface. Replace with `StubShellBlueprintProvider` (T019/T036) in every consuming integration test.
- [X] T084 Run `grep -rn "RegisterBlueprint\|GetBlueprintNames\|ReloadAllAsync\|GetBlueprintsAsync" src/ tests/ samples/` and confirm zero hits. Any remaining hits must be addressed before the phase closes.
- [X] T085 Run full `dotnet build` and `dotnet test` from repo root; all projects must build clean, all tests must pass. Depends on T058–T084.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Docs, validation, and final quality pass.

- [X] T086 [P] Add/update XML `<remarks>` on every new public type in `CShells.Abstractions/Lifecycle/` per Constitution Principle VI (architectural constraints not obvious from signature).
- [ ] T087 [P] Update `src/CShells.Abstractions/README.md` and `src/CShells/README.md` with one-paragraph summaries of the new surface and a pointer to `specs/007-blueprint-provider-split/quickstart.md`.
- [X] T088 [P] Update `wiki/Shell-Reload-Semantics.md` (if it references `ReloadAllAsync`) and `wiki/FastEndpoints-Integration.md` (if it describes registry activation flow).
- [ ] T089 Execute `quickstart.md` sections 1–10 against a fresh local build (spin up `CShells.Workbench`, try each configuration); file bugs as new tasks if any section diverges from actual behavior.
- [X] T090 Enable `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (if temporarily disabled during T028) and confirm clean build.
- [X] T091 Rerun full `dotnet test`; confirm 303+ tests pass (existing 303 + new US1–US5 + new provider tests). Verify SC-006 (every 006 scenario still passes).

**Checkpoint**: Feature 007 ready to merge.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: no dependencies.
- **Phase 2 Foundational** (T003–T018): depends on Phase 1. BLOCKS all user-story phases.
- **Phase 3 US1** (T019–T035): depends on Phase 2. Delivers the MVP. All subsequent user stories depend on US1's registry rewrite.
- **Phase 4 US2** (T036–T046): depends on T024 (registry rewrite) and T023 (composite skeleton).
- **Phase 5 US3** (T047–T057): depends on T023. Can run in parallel with Phase 4 (different files).
- **Phase 6 US4** (T058–T065): depends on T024. Can run in parallel with Phases 4–5.
- **Phase 7 US5** (T066–T070): depends on T024 and T023. Can run in parallel with Phases 4–6.
- **Phase 8 Migration** (T071–T078): depends on Phase 2 abstractions only for types, but most practically on Phase 3 (needs composite + registry working to integration-test).
- **Phase 9 Cleanup** (T079–T085): depends on Phases 3–8 being complete — delete only after replacements are in place and verified.
- **Phase 10 Polish** (T086–T091): depends on Phase 9.

### Within Each User Story

- Tests are written BEFORE implementation; they are expected to fail initially and pass once the implementation tasks complete.
- Models/records before services.
- Registry modifications serialize on T024 — one person works that file at a time across the US1/US2/US5/US7 registry tasks.

### Parallel Opportunities

- **All Phase 2 tasks T003–T016** are parallel (one file each).
- **US1 test scaffolding T019–T021** is parallel.
- **US1 integration tests T029–T035** are parallel (different files).
- **US2 integration tests T040–T046** are parallel.
- **US3 integration tests T053–T057** are parallel.
- **US4 integration tests T062–T065** are parallel.
- **US5 integration tests T067–T070** are parallel.
- **Phase 9 cleanup deletions T079–T083** are parallel.
- **Phase 10 doc updates T086–T088** are parallel.

### Cross-story file overlap (serialize)

- `src/CShells/Lifecycle/ShellRegistry.cs` — T024 (US1), T038 (US2), T039 (US2), T066 (US5). Same file; do these sequentially.
- `src/CShells/Lifecycle/Providers/CompositeShellBlueprintProvider.cs` — T023 (US1), T049 (US3), T050 (US3), T051 (US3). Same file; sequential.

---

## Parallel Example: Phase 2 Foundational

```bash
# Launch all 14 abstraction-type tasks together — each is a distinct new file:
Task: "Create ProvidedBlueprint in ProvidedBlueprint.cs"
Task: "Create BlueprintListQuery in BlueprintListQuery.cs"
Task: "Create BlueprintPage in BlueprintPage.cs"
Task: "Create BlueprintSummary in BlueprintSummary.cs"
Task: "Create ShellListQuery in ShellListQuery.cs"
Task: "Create ShellPage in ShellPage.cs"
Task: "Create ShellSummary in ShellSummary.cs"
Task: "Create ReloadOptions in ReloadOptions.cs"
Task: "Create ShellBlueprintNotFoundException in ShellBlueprintNotFoundException.cs"
Task: "Create ShellBlueprintUnavailableException in ShellBlueprintUnavailableException.cs"
Task: "Create BlueprintNotMutableException in BlueprintNotMutableException.cs"
Task: "Create DuplicateBlueprintException in DuplicateBlueprintException.cs"
Task: "Rewrite IShellBlueprintProvider in IShellBlueprintProvider.cs"
Task: "Create IShellBlueprintManager in IShellBlueprintManager.cs"

# IShellRegistry (T017) runs once these 14 land — its docs reference the new types.
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 → Phase 2 → Phase 3.
2. At T035 checkpoint, the library supports lazy activation against code-seeded blueprints. Feature-`006` tests that touched removed registry members still fail — expected; they get fixed in Phase 8/9.
3. Commit and open PR as a draft; run CI on the branch; triage failing 006 tests.

### Incremental Delivery

1. **MVP (US1)**: lazy activation in-memory. Green US1 test file.
2. **+ US2**: manager contract wired; `UnregisterBlueprintAsync` works end-to-end.
3. **+ US3**: composite hardened with cursor codec + duplicate detection.
4. **+ US4**: ASP.NET Core middleware translates provider exceptions; first-request activation works in TestHost.
5. **+ US5**: `ListAsync` joins catalogue + lifecycle; contract ready for feature `009`.
6. **+ Migration**: `ConfigurationShellBlueprintProvider` + `FluentStorageShellBlueprintProvider` on the new contract.
7. **+ Cleanup**: legacy surface deleted.
8. **+ Polish**: docs + warnings-as-errors + full test pass.

### Single-Developer Sequencing

The realistic serialization given that one person will touch `ShellRegistry.cs` in multiple phases:

```
Phase 1 → Phase 2 → T019–T023 (parallel) → T024 (register rewrite) → T025–T028 (wiring) →
T029–T035 (US1 tests) → T036–T039 (US2 impls) → T040–T046 (US2 tests) →
T047–T057 (US3 end-to-end) → T058–T065 (US4 end-to-end) → T066–T070 (US5 end-to-end) →
T071–T078 (migration) → T079–T085 (cleanup) → T086–T091 (polish).
```

### Parallel Team Strategy

With 2–3 developers:

1. Whole team does Phase 1 + Phase 2 together (abstraction alignment).
2. Developer A takes Phase 3 (US1 core registry + providers) — critical path.
3. Developer B takes Phase 5 (US3 cursor codec + duplicate detection) once T023 lands.
4. Developer C takes Phase 6 (US4 ASP.NET middleware) once T024 lands.
5. A rolls US2 (Phase 4) after US1 stabilizes; B picks up US5 (Phase 7) after US3.
6. Team splits Phase 8 migration by provider (A: Configuration, B: FluentStorage).
7. Whole team reviews Phase 9 cleanup sweeps together.

---

## Notes

- `[P]` tasks are different files with no incomplete-task dependency at the moment of launch.
- Every user-story phase is independently testable — its integration test file is green after its tasks complete, regardless of whether other stories have shipped.
- `tests/CShells.Tests/TestHelpers/StubShellBlueprintProvider.cs` is intentionally a shared helper; it is created in T019 and extended in T036, and every US1–US5 test uses it.
- Commits should be atomic at task boundaries (or at small task clusters) to keep `git bisect` useful.
- Per Constitution Principle VI: do not retain legacy types, shims, or deprecated paths. Phase 9 is mandatory.
