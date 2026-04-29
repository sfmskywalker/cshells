---
description: "Task list for Blueprint-Aware Path Routing — restoring lazy activation through the routing layer"
---

# Tasks: Blueprint-Aware Path Routing

**Input**: Design documents from `/specs/010-blueprint-aware-routing/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Scope**: Add `IShellRouteIndex` to `CShells.AspNetCore.Abstractions` and a default implementation in `CShells.AspNetCore`. Migrate `IShellResolverStrategy.Resolve` → `ResolveAsync` (the single breaking change). Rewrite `WebRoutingShellResolver` to consult the index instead of `IShellRegistry.GetActiveShells()`. Wire `ShellRouteIndexInvalidator` to lifecycle notifications. Update `CShellsStartupHostedService` log copy. Drop `PreWarmShells` from the Workbench sample. Tests for SC-001…SC-008.

**Tests**: Included (Constitution Principle V). Each user story phase includes unit and/or integration tests. Existing 005-009 test suites MUST continue to pass after this feature (SC-006).

**Organization**: Phase 1 scaffolds. Phase 2 lands new abstractions and the `IShellResolverStrategy` async migration. Phases 3–7 implement user stories in priority order. Phase 8 migrates in-tree consumers (Workbench, log copy). Phase 9 deletes the old resolver helpers. Phase 10 polishes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story this task belongs to (US1…US5). Tasks not tied to a story have no label.
- File paths are repo-root relative.

## Path Conventions

- **Abstractions**: `src/CShells.AspNetCore.Abstractions/Routing/`, `src/CShells.AspNetCore.Abstractions/Resolution/`
- **Implementation**: `src/CShells.AspNetCore/Routing/`, `src/CShells.AspNetCore/Resolution/`, `src/CShells.AspNetCore/Middleware/`, `src/CShells/Hosting/`
- **Tests**: `tests/CShells.Tests/Unit/AspNetCore/Routing/`, `tests/CShells.Tests/Integration/AspNetCore/`, `tests/CShells.Tests.EndToEnd/Routing/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create directory scaffolding for the new routing abstraction and tests.

- [ ] T001 Create directory `src/CShells.AspNetCore.Abstractions/Routing/`
- [ ] T002 [P] Create directory `src/CShells.AspNetCore/Routing/`
- [ ] T003 [P] Create directory `tests/CShells.Tests/Unit/AspNetCore/Routing/`
- [ ] T004 [P] Create directory `tests/CShells.Tests.EndToEnd/Routing/`

---

## Phase 2: Foundational Abstractions (Blocking Prerequisites)

**Purpose**: Land every new public type in `CShells.AspNetCore.Abstractions` and migrate `IShellResolverStrategy` to async. These MUST compile cleanly before any implementation or user-story work begins.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

### Value types

- [ ] T005 [P] Create `ShellRouteEntry` record (`ShellName`, `Path`, `Host`, `HeaderName`, `ClaimKey`) with `Guard.Against.NullOrWhiteSpace(ShellName)` in `src/CShells.AspNetCore.Abstractions/Routing/ShellRouteEntry.cs` (data-model §`ShellRouteEntry`).
- [ ] T006 [P] Create `ShellRouteCriteria` record (`PathFirstSegment`, `IsRootPath`, `Host`, `HeaderName`, `HeaderValue`, `ClaimKey`, `ClaimValue`) in `src/CShells.AspNetCore.Abstractions/Routing/ShellRouteCriteria.cs` (data-model §`ShellRouteCriteria`).
- [ ] T007 [P] Create `ShellRoutingMode` enum (`Path`, `RootPath`, `Host`, `Header`, `Claim`) in `src/CShells.AspNetCore.Abstractions/Routing/ShellRoutingMode.cs` (data-model §`ShellRouteMatch`).
- [ ] T008 [P] Create `ShellRouteMatch` record (`ShellId`, `MatchedMode`) in `src/CShells.AspNetCore.Abstractions/Routing/ShellRouteMatch.cs`. Depends on T007.

### Exception

- [ ] T009 [P] Create `ShellRouteIndexUnavailableException : Exception` (constructor takes `message` + `innerException`) in `src/CShells.AspNetCore.Abstractions/Routing/ShellRouteIndexUnavailableException.cs` (contracts/Exceptions.md).

### Interface

- [ ] T010 Create `IShellRouteIndex` interface (`TryMatchAsync`, `GetCandidateSnapshot`) in `src/CShells.AspNetCore.Abstractions/Routing/IShellRouteIndex.cs`. Full XML docs per contracts/IShellRouteIndex.md. Depends on T005–T009.

### Resolver async migration (the one breaking change)

- [ ] T011 **Modify** `IShellResolverStrategy` in `src/CShells.AspNetCore.Abstractions/Resolution/IShellResolverStrategy.cs`: replace `ShellId? Resolve(ShellResolutionContext context)` with `Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken)`. Full XML docs per contracts/IShellResolverStrategy.md. (No default-interface-method shim.)

### Verification

- [ ] T012 Run `dotnet build src/CShells.AspNetCore.Abstractions/CShells.AspNetCore.Abstractions.csproj`; confirm zero errors / zero warnings across `net8.0`, `net9.0`, `net10.0`. Depends on T005–T011.

**Checkpoint**: Abstractions compile. `src/CShells.AspNetCore/` does NOT compile yet (`WebRoutingShellResolver` and `DefaultShellResolverStrategy` still implement the deleted sync `Resolve`). User stories proceed.

---

## Phase 3: User Story 1 — Cold blueprint auto-activates on first request (Priority: P1) 🎯 MVP

**Goal**: A host with one or more configured blueprints serves the first matching request without any prior `PreWarmShells` call. The route index discovers the blueprint via `IShellBlueprintProvider`, returns the matching `ShellId`, `ShellMiddleware` lazily activates the shell, endpoints register, and the response is served.

**Independent Test**: Configure two shells via `StubShellBlueprintProvider` (`Default` at path `""`, `acme` at path `"acme"`); start the host without calling `.PreWarmShells(...)`; assert `_registry.GetActiveShells()` is empty; issue `GET /` → assert it activates `Default` and returns `200`; issue `GET /acme/x` → assert it activates `acme` and returns `200`; assert each provider lookup happens exactly once per shell name.

### Test doubles and unit tests for US1

- [ ] T013 [P] [US1] Create `StubShellRouteEntrySource` test helper in `tests/CShells.Tests/TestHelpers/StubShellRouteEntrySource.cs` — wraps a `Dictionary<string, ShellRouteEntry>`, exposes a `Provider` property returning a `StubShellBlueprintProvider` (the existing helper from feature 007's tests) configured to return blueprints whose `Properties["WebRouting"]` reflect the entries. Counter for `GetAsync` and `ListAsync` calls.
- [ ] T014 [P] [US1] Unit tests for `ShellRouteIndexBuilder` (extraction of `WebRouting:*` from a stub `IShellBlueprint.Properties`, null-vs-empty distinction, leading-`/` rejection) in `tests/CShells.Tests/Unit/AspNetCore/Routing/ShellRouteIndexBuilderTests.cs`.
- [ ] T015 [P] [US1] Unit tests for `DefaultShellRouteIndex` snapshot construction (`Path` map, `Host` map, `Header`/`Claim` value maps, root-path single-claimant, root-path ambiguous flag) in `tests/CShells.Tests/Unit/AspNetCore/Routing/DefaultShellRouteIndexTests.cs`.

### Core implementations for US1

- [ ] T016 [US1] Implement internal `ShellRouteIndexBuilder` (static, in `src/CShells.AspNetCore/Routing/ShellRouteIndexBuilder.cs`): given an `IShellBlueprint`, return either a `ShellRouteEntry` (with the four optional routing fields populated from `blueprint.Properties.GetSection("WebRouting")["Path"|"Host"|"HeaderName"|"ClaimKey"]`) OR a builder-level rejection for `Path` starting with `/` (logged at WARN per R-005, entry omitted). Internal — `internal sealed class`. Depends on T014.
- [ ] T017 [US1] Implement `DefaultShellRouteIndex` in `src/CShells.AspNetCore/Routing/DefaultShellRouteIndex.cs` (`internal sealed class : IShellRouteIndex`). Inject `IShellBlueprintProvider`, `ILogger<DefaultShellRouteIndex>`. Hold `Volatile`-published `ShellRouteIndexSnapshot?` (initially null). Hold `SemaphoreSlim(1,1)` for refresh serialization. `TryMatchAsync` logic per contracts/IShellRouteIndex.md (path-by-name uses `provider.GetAsync(segment)` directly; other modes go through the snapshot, populating on first need). `GetCandidateSnapshot(maxEntries)` returns up to `maxEntries` entries from the current snapshot's `All`. Includes `RefreshAsync(name?)` internal method that adds/replaces a single entry or rebuilds the whole snapshot when `name` is null. Depends on T015, T016.
- [ ] T018 [US1] Rewrite `WebRoutingShellResolver` at `src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs`:
  - Switch primary constructor to `(IShellRouteIndex routeIndex, WebRoutingShellResolverOptions options, ILogger<WebRoutingShellResolver> logger)` — drop the `IShellRegistry` dependency.
  - Implement `Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken ct)` per contracts/IShellResolverStrategy.md "After" section.
  - Add private `BuildCriteria(ShellResolutionContext)` that mirrors today's path-segment-extract / host / header / claim logic and produces a `ShellRouteCriteria`. Honour all existing `WebRoutingShellResolverOptions` knobs (`EnablePathRouting`, `EnableHostRouting`, `HeaderName`, `ClaimKey`, `ExcludePaths`).
  - Catch `ShellRouteIndexUnavailableException`, log Warning, return null.
  - Delete `TryResolveByPath`, `TryResolveByHost`, `TryResolveByHeader`, `TryResolveByClaim`, `TryResolveByRootPath`, `FindMatchingShell`, `FindMatchingShellByIdentifier`, `ActiveShells` helpers — all replaced by index lookup.
  - Defer the no-match log entry (T038) and per-match log entry (T039) to Phase 7; for now, return null silently when no match.
  Depends on T017, T011.
- [ ] T019 [US1] Modify `DefaultShellResolverStrategy` at `src/CShells/Resolution/DefaultShellResolverStrategy.cs` (or wherever it lives — confirm path during impl): trivial sync→async migration per contracts/IShellResolverStrategy.md (`Task.FromResult<ShellId?>(new ShellId("Default"))`). Depends on T011.
- [ ] T020 [US1] Modify `ShellMiddleware` at `src/CShells.AspNetCore/Middleware/ShellMiddleware.cs`: `_resolver.Resolve(...)` → `await _resolver.ResolveAsync(context, context.RequestAborted)`. No other behavioural change. Depends on T011.
- [ ] T021 [US1] Modify `ServiceCollectionExtensions` in `src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs`: register `IShellRouteIndex` → `DefaultShellRouteIndex` as singleton. Depends on T017.
- [ ] T022 [US1] Verify `dotnet build src/CShells.AspNetCore/CShells.AspNetCore.csproj` completes cleanly. Depends on T016–T021.

### Integration tests for US1

- [ ] T023 [P] [US1] Integration test `Resolve_ColdRootPathBlueprint_ReturnsShellId_AndMiddlewareActivatesIt` in `tests/CShells.Tests/Integration/AspNetCore/WebRoutingShellResolverLazyActivationTests.cs` (acceptance scenario 1.1, SC-001).
- [ ] T024 [P] [US1] Integration test `Resolve_ColdPathSegmentBlueprint_ReturnsShellId_AndMiddlewareActivatesIt` in same file (acceptance scenario 1.2, SC-001).
- [ ] T025 [P] [US1] Integration test `Resolve_UnknownPathSegment_ReturnsNull_AndDoesNotActivateAnyShell` in same file (acceptance scenario 1.3).
- [ ] T026 [P] [US1] Integration test `Resolve_WhenProviderThrows_PropagatesAsRouteIndexUnavailable_OnFirstNonNameModeUse_ButPathByNameStillWorks` in same file (acceptance scenario 1.4 + R-001 split).
- [ ] T027 [P] [US1] Integration test `ProviderGetAsync_ForPathByNameRouting_IsCalledExactlyOncePerColdShell` in same file (instrument `StubShellBlueprintProvider.GetAsyncCallCount`).

**Checkpoint**: MVP complete. `WebRoutingShellResolverLazyActivationTests.cs` is green. The Workbench sample serves requests without `PreWarmShells` — but the explicit `PreWarmShells` removal in Workbench `Program.cs` lands in Phase 8.

---

## Phase 4: User Story 2 — Reloaded shell auto-activates on next request (Priority: P1)

**Goal**: After `IShellRegistry.ReloadAsync(name)` drains a generation, the next matching request lazily activates the new generation. The route index does NOT need to be invalidated for this case (the blueprint hasn't changed) — the fix flows entirely through `ShellMiddleware.GetOrActivateAsync` calling into the registry, which builds the next generation.

**Independent Test**: Pre-warm or lazy-activate `Default`; confirm a request returns `200`; call `IShellRegistry.ReloadAsync("Default")` (or hit `/elsa/api/shells/reload` via TestHost); assert the registry holds zero active shells immediately after; issue another request; assert it activates `Default#2` and returns `200`.

### Lifecycle invalidator (also covers blueprint-change reload — see Phase 4.b)

- [ ] T028 [P] [US2] Unit tests for `ShellRouteIndexInvalidator` (handles `ShellAdded` → calls `index.RefreshAsync(name)`, handles `ShellRemoved` → calls `index.RemoveAsync(name)`, handles `ShellReloaded` → calls `index.RefreshAsync(name)`, does NOT subscribe to `ShellActivated`/`ShellDeactivating`) in `tests/CShells.Tests/Unit/AspNetCore/Routing/ShellRouteIndexInvalidatorTests.cs`.
- [ ] T029 [US2] Implement `ShellRouteIndexInvalidator` in `src/CShells.AspNetCore/Routing/ShellRouteIndexInvalidator.cs` (`internal sealed class`). Implements `INotificationHandler<ShellAdded>`, `INotificationHandler<ShellRemoved>`, `INotificationHandler<ShellReloaded>` (or whatever the post-006 notification subscriber types are — confirmed during impl per R-009). Forwards to `IShellRouteIndex` incremental update methods. Catches and logs its own exceptions per Principle VII subscriber-isolation. Depends on T017, T028.
- [ ] T030 [US2] Extend `IShellRouteIndex` (in T010) and `DefaultShellRouteIndex` (in T017) with internal `RefreshAsync(string name, CancellationToken ct)` and `RemoveAsync(string name, CancellationToken ct)` methods used by the invalidator. These take the per-name semaphore (NOT the global refresh semaphore) and update the snapshot via copy-on-write of the affected dictionaries. Depends on T017.
- [ ] T031 [US2] Register `ShellRouteIndexInvalidator` in `src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs` (alongside the index registration from T021). Depends on T029.

### Integration tests for US2

- [ ] T032 [P] [US2] Integration test `Reload_ActiveShell_ThenNextRequest_ActivatesNewGeneration_AndIsServed` in `tests/CShells.Tests/Integration/AspNetCore/WebRoutingShellResolverPostReloadTests.cs` (acceptance scenario 2.1, SC-002).
- [ ] T033 [P] [US2] Integration test `Reload_RepeatedlyForSameShell_LazyReactivation_HappensEachTime` in same file (acceptance scenario 2.2).
- [ ] T034 [P] [US2] Integration test `Reload_OneShellOnly_DoesNotForceReactivationOfOtherShells` in same file (acceptance scenario 2.3).

**Checkpoint**: The post-reload 404 regression is fixed. `WebRoutingShellResolverPostReloadTests.cs` green.

### Phase 4.b: blueprint mutation lifecycle (validates US2's invalidator)

- [ ] T035 [P] [US2] Integration test `BlueprintAdded_AtRuntime_NextRequest_RoutesToNewBlueprint` in same file (covers the runtime-add edge case from spec.md §Edge Cases).
- [ ] T036 [P] [US2] Integration test `BlueprintRemoved_AtRuntime_NextRequest_DoesNotRouteToRemovedBlueprint` in same file.
- [ ] T037 [P] [US2] Integration test `BlueprintReloaded_WithChangedRoutingPath_OldPathStopsRouting_NewPathStartsRouting` in same file.

---

## Phase 5: User Story 5 — Diagnostic logging on resolution outcomes (Priority: P3)

**Goal**: Every unmatched request produces exactly one structured log entry naming the considered routing values and a bounded representation of candidate blueprints. Per-match logs are gated behind `WebRoutingShellResolverOptions.LogMatches`. Promoted from US3/US4 because logging is fully decoupled from the routing-correctness work and unblocks operator diagnosis.

**Independent Test**: Configure a single shell at path `"acme"`; issue `GET /unknown/x`; assert the resolver emits exactly one `Information`-level log entry containing the path, the routing modes attempted, and the blueprint `acme(Path="acme")`. Configure 100 blueprints; issue an unmatched request; assert the entry includes at most `NoMatchLogCandidateCap` entries plus a `(+N more)` indicator.

### Options surface

- [ ] T038 [US5] Add `int NoMatchLogCandidateCap { get; set; } = 10;` and `bool LogMatches { get; set; } = false;` to existing `WebRoutingShellResolverOptions` at `src/CShells.AspNetCore/Resolution/WebRoutingShellResolverOptions.cs` (R-007).

### Resolver logging

- [ ] T039 [US5] Modify `WebRoutingShellResolver.ResolveAsync` (from T018): on `null` outcome, call `IShellRouteIndex.GetCandidateSnapshot(_options.NoMatchLogCandidateCap + 1)`, build a structured log entry with the path / host / header / claim values from `criteria` and a comma-separated list of candidate `ShellName(mode=value)` items capped at `NoMatchLogCandidateCap` with a `(+N more)` suffix when truncated. Log at `Information` level. On non-null outcome, if `LogMatches`, log at `Debug` level naming the matched `ShellId` and `MatchedMode`. Depends on T038, T018.

### Integration tests for US5

- [ ] T040 [P] [US5] Integration test `UnmatchedRequest_EmitsExactlyOneInformationLogEntry_NamingPathAndCandidates` in `tests/CShells.Tests/Integration/AspNetCore/WebRoutingShellResolverDiagnosticsTests.cs` using the xUnit `XunitLoggerProvider` pattern or `FakeLogger<WebRoutingShellResolver>`.
- [ ] T041 [P] [US5] Integration test `UnmatchedRequest_With100Candidates_TruncatesAt10_AndIncludesPlusNMoreSuffix` in same file (acceptance scenario 5.2).
- [ ] T042 [P] [US5] Integration test `MatchedRequest_WithLogMatchesFalse_EmitsNoLogEntry` in same file.
- [ ] T043 [P] [US5] Integration test `MatchedRequest_WithLogMatchesTrue_EmitsExactlyOneDebugLogEntry_NamingShellIdAndMode` in same file.

**Checkpoint**: Operator diagnostics complete. Silent 404s eliminated.

---

## Phase 6: User Story 4 — Scaling preserved at 100k blueprints (Priority: P2)

**Goal**: Confirm the route index does not enumerate the catalogue at startup and that path-by-name routing pays at most one provider lookup per cold blueprint per host process. Pre-existing `007` SC-001 promise stays intact.

**Independent Test**: Configure a `StubShellBlueprintProvider` whose `ListAsync` is instrumented to throw if called more than once; start the host with no pre-warm; assert startup completes; issue a path-by-name request; assert the request is served and `ListAsync` was never called (path-by-name uses `GetAsync`); issue a root-path request; assert `ListAsync` was called exactly once (initial snapshot population).

### Tests for US4

- [ ] T044 [P] [US4] Integration test `Startup_With100kBlueprints_DoesNotInvokeListAsync_OrGetAsync` in `tests/CShells.Tests/Integration/AspNetCore/WebRoutingShellResolverScalingTests.cs` (SC-004 part 1).
- [ ] T045 [P] [US4] Integration test `PathByNameRouting_For100ColdShells_Calls100GetAsync_AndZeroListAsync` in same file.
- [ ] T046 [P] [US4] Integration test `RootPathOrHostMode_FirstUse_TriggersListAsync_ExactlyOnce_SubsequentRequestsServeFromSnapshot` in same file.

**Checkpoint**: Scaling promise verified. No code changes required for US4 — it's a property of the design pinned by R-001.

---

## Phase 7: User Story 3 — `PreWarmShells` preserved as a perf hint (Priority: P2)

**Goal**: Confirm `CShellsBuilder.PreWarmShells(...)` still activates listed shells at startup, and that omitting it no longer breaks routing. Includes the `CShellsStartupHostedService` log-line rephrasing.

**Independent Test**: Configure two shells; call `.PreWarmShells("Default")`; assert immediately after `app.RunAsync()` returns that `_registry.GetActiveShells()` contains `Default` only; assert the first request to `acme` still activates `acme` lazily.

### Log line rephrasing

- [ ] T047 [US3] Modify `CShellsStartupHostedService` (path confirmed at impl time per R-009) to replace the `"registry remains idle until first activation"` log message with `"CShells startup: no shells pre-warmed; routing will activate shells lazily on first request."` (Information level). The existing pre-warm-list-non-empty branch (`"pre-warming N shell(s)"`) stays unchanged.

### Tests for US3

- [ ] T048 [P] [US3] Integration test `PreWarmShells_ActivatesListedNamesAtStartup_OmittedNamesRemainCold` (regression-confirm — extends or duplicates existing `ShellRegistryPreWarmTests` from feature 007 if needed) in `tests/CShells.Tests/Integration/Lifecycle/PreWarmShellsRegressionTests.cs`.
- [ ] T049 [P] [US3] Integration test `PreWarmShells_WithUnknownName_LogsWarning_AndDoesNotBlockOtherNames_AndLazyActivationStillWorksForKnownNames` in same file (acceptance scenario 3.3).
- [ ] T050 [P] [US3] Unit test `StartupHostedService_WithEmptyPreWarmList_LogsLazyActivationLanguage_NotRegistryRemainsIdleLanguage` in `tests/CShells.Tests/Unit/Hosting/CShellsStartupHostedServiceTests.cs` (FR-017).

**Checkpoint**: Pre-warm preserved. Misleading log line replaced.

---

## Phase 8: Migration of in-tree consumers

**Purpose**: Update the Workbench sample to demonstrate the new lazy-by-default behaviour. Update docs.

- [ ] T051 Update `samples/CShells.Workbench/Program.cs`: remove any `.PreWarmShells(...)` call. Add a brief comment near the `.AddShells(...)` call: `// Routing activates shells lazily on first request; PreWarmShells is now a perf hint only.`
- [ ] T052 Update `samples/CShells.Workbench/README.md` to mention the lazy-by-default behaviour and link to `quickstart.md`.
- [ ] T053 [P] Update `wiki/Shell-Reload-Semantics.md` (if it exists and references the old "registry remains idle" log line or implies pre-warm is required for routing) to reflect the new behaviour.
- [ ] T054 [P] Update `wiki/FastEndpoints-Integration.md` (if it references the resolver flow) to reflect the new async resolver signature.

---

## Phase 9: Cleanup — Delete superseded helpers

**Purpose**: Remove the active-shells iteration helpers in `WebRoutingShellResolver`. They were already deleted in T018; this phase verifies no stragglers remain.

- [ ] T055 Run `grep -rn "FindMatchingShell\|FindMatchingShellByIdentifier\|ActiveShells\(\)\b" src/CShells.AspNetCore/` and confirm zero hits in `Resolution/`. Any remaining hits (e.g., in a test helper that mocked the old shape) must be addressed.
- [ ] T056 Run `grep -rn "GetActiveShells\b" src/CShells.AspNetCore/` and confirm hits are scoped to legitimate uses (e.g., diagnostics, admin lists), NOT the resolver hot path. Document any remaining usage.
- [ ] T057 Run `grep -rn "ShellId? Resolve(\b\|public ShellId? Resolve\b" src/ tests/` and confirm zero hits — every implementation has migrated to `ResolveAsync`.
- [ ] T058 Run full `dotnet build` and `dotnet test` from repo root; all projects must build clean, all tests must pass. Depends on T002–T057.

---

## Phase 10: End-to-End and Polish

**Purpose**: Full HTTP cold-start + reload cycle test in `tests/CShells.Tests.EndToEnd/`. Final docs and quickstart sanity check.

- [ ] T059 [P] End-to-end test `ColdStart_FirstRequest_ActivatesShell_AndServes200` in `tests/CShells.Tests.EndToEnd/Routing/ColdStartReloadCycleTests.cs` using `WebApplicationFactory<Program>` over the Workbench sample (SC-001 + SC-007).
- [ ] T060 [P] End-to-end test `ColdStart_FirstRequest_Reload_NextRequest_ActivatesNewGeneration_AndServes200` in same file (SC-002).
- [ ] T061 [P] End-to-end test `ColdStart_ReloadCycle_RepeatedTenTimes_AlwaysServes200_WithIncrementingGenerations` in same file (regression-hardening for the second-call-404 we observed in the field).
- [ ] T062 Update XML `<remarks>` on `IShellRouteIndex` and `ShellRouteEntry` if any architectural constraint not obvious from the signature emerged during implementation (Constitution Principle VI).
- [ ] T063 Update `src/CShells.AspNetCore.Abstractions/README.md` and `src/CShells.AspNetCore/README.md` with a one-paragraph summary of the new routing surface and a pointer to `specs/010-blueprint-aware-routing/quickstart.md`.
- [ ] T064 Execute `quickstart.md` sections 1–7 against a fresh local build (spin up `CShells.Workbench`, exercise each example); file follow-up tasks if any section diverges from observed behaviour.
- [ ] T065 Confirm `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is in effect across the modified projects.
- [ ] T066 Rerun full `dotnet test`; record before/after pass counts in the PR description. Confirm SC-006 (every existing 005-009 test scenario still passes).

**Checkpoint**: Feature 010 ready to merge.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: no dependencies.
- **Phase 2 Foundational** (T005–T012): depends on Phase 1. BLOCKS all user-story phases.
- **Phase 3 US1** (T013–T027): depends on Phase 2. Delivers the MVP (cold-blueprint lazy activation through routing).
- **Phase 4 US2** (T028–T037): depends on T017 (index implementation) and T011 (async resolver contract). Adds the lifecycle invalidator; covers post-reload + runtime add/remove/change.
- **Phase 5 US5** (T038–T043): depends on T018, T038. Diagnostic logging.
- **Phase 6 US4** (T044–T046): depends on T017. Pure verification — no implementation.
- **Phase 7 US3** (T047–T050): depends on T017 (lazy activation must work) and T011 only via the existing pre-warm path. Mostly verification + the log-line copy change in T047.
- **Phase 8 Migration** (T051–T054): depends on Phase 3 (cold-routing must work before we drop the Workbench's `PreWarmShells`).
- **Phase 9 Cleanup** (T055–T058): depends on Phases 3–8.
- **Phase 10 Polish + E2E** (T059–T066): depends on Phase 9.

### Within Each User Story

- Tests are written BEFORE implementation; they are expected to fail initially and pass once the implementation tasks complete.
- Records before services. Interface (T010) before implementation (T017).
- `WebRoutingShellResolver.cs` is the resolver-rewrite battleground — T018 (US1) and T039 (US5) modify the same file. Sequence T018 → T039.
- `DefaultShellRouteIndex.cs` is touched by T017 (US1) and T030 (US2 — adds `RefreshAsync`/`RemoveAsync`). Sequence T017 → T030.
- `ServiceCollectionExtensions.cs` is touched by T021 (US1 — register index) and T031 (US2 — register invalidator). Sequence T021 → T031.

### Parallel Opportunities

- **Phase 2 value types T005–T009** are parallel (one new file each).
- **US1 unit-test scaffolding T013–T015** is parallel.
- **US1 integration tests T023–T027** are parallel (new file).
- **US2 lifecycle tests T032–T037** are parallel.
- **US5 diagnostic tests T040–T043** are parallel.
- **US4 scaling tests T044–T046** are parallel.
- **US3 pre-warm tests T048–T050** are parallel.
- **Phase 8 doc updates T053, T054** are parallel.
- **Phase 10 E2E tests T059–T061** are parallel.

### Cross-story file overlap (serialize)

- `src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs` — T018 (US1), T039 (US5). Sequential.
- `src/CShells.AspNetCore/Routing/DefaultShellRouteIndex.cs` — T017 (US1), T030 (US2). Sequential.
- `src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs` — T021 (US1), T031 (US2). Sequential.

---

## Parallel Example: Phase 2 Foundational

```bash
# Launch the five new-type tasks together — distinct files:
Task: "Create ShellRouteEntry in ShellRouteEntry.cs"
Task: "Create ShellRouteCriteria in ShellRouteCriteria.cs"
Task: "Create ShellRoutingMode enum in ShellRoutingMode.cs"
Task: "Create ShellRouteIndexUnavailableException in ShellRouteIndexUnavailableException.cs"
# After T007 (the enum) lands:
Task: "Create ShellRouteMatch in ShellRouteMatch.cs"
# After all five land:
Task: "Create IShellRouteIndex interface in IShellRouteIndex.cs"
Task: "Modify IShellResolverStrategy to async signature"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 → Phase 2 → Phase 3.
2. At T027 the SC-001 regression is fixed: cold blueprint resolves on first request, lazy activation works through routing.
3. Open the PR as a draft, push the branch, run CI. The remaining phases land as additional commits.

### Incremental Delivery

1. **MVP (US1)**: cold-blueprint lazy activation. Green `WebRoutingShellResolverLazyActivationTests`.
2. **+ US2**: post-reload + runtime add/remove/change. Green `WebRoutingShellResolverPostReloadTests`.
3. **+ US5**: diagnostic logging. Green `WebRoutingShellResolverDiagnosticsTests`.
4. **+ US4**: scaling assertions. Green `WebRoutingShellResolverScalingTests`.
5. **+ US3**: pre-warm regression + log-line copy. Green pre-warm regression tests.
6. **+ Migration**: Workbench `Program.cs` drops `PreWarmShells`; doc updates.
7. **+ Cleanup**: `grep` sweeps confirm no resolver-internal helpers linger.
8. **+ E2E + Polish**: cold-start/reload cycle in `WebApplicationFactory`; READMEs updated; quickstart walked.

### Single-Developer Sequencing

Realistic order given the file-overlap constraints:

```
Phase 1 → Phase 2 → T013–T015 (parallel unit tests) →
T016 (builder) → T017 (index core) → T018 (resolver async + index lookup) →
T019 (DefaultShellResolverStrategy migration) → T020 (middleware) →
T021 (DI registration) → T022 (build) →
T023–T027 (US1 integration tests) →
T028 (invalidator unit tests) → T029–T031 (invalidator + DI) → T032–T037 (US2 tests) →
T038 (options) → T039 (resolver logging) → T040–T043 (US5 tests) →
T044–T046 (US4 scaling tests) →
T047 (startup log copy) → T048–T050 (US3 tests) →
T051–T054 (Workbench + docs) →
T055–T058 (cleanup grep + full build/test) →
T059–T061 (E2E) → T062–T066 (polish).
```

### Parallel Team Strategy

With 2–3 developers:

1. Phase 1 + Phase 2 together — one developer drives the abstraction landing while others review.
2. Developer A takes Phase 3 (route index + resolver rewrite) — critical path.
3. Developer B takes Phase 4 (lifecycle invalidator + tests) once T017 lands.
4. Developer C takes Phase 5 (diagnostic logging) once T018 lands.
5. Developer A picks up Phase 7 (pre-warm regression + log line) while B/C finish their phases.
6. Whole team reviews Phase 8 (Workbench + docs) and Phase 10 (E2E).

---

## Notes

- `[P]` tasks are different files with no incomplete-task dependency at the moment of launch.
- Every user-story phase is independently testable — its integration test file is green after its tasks complete, regardless of whether other stories have shipped.
- `tests/CShells.Tests/TestHelpers/StubShellBlueprintProvider.cs` is REUSED from feature 007's test infrastructure (created in 007 T019). T013 wraps it in a routing-friendly façade rather than duplicating it.
- Commits should be atomic at task boundaries (or small task clusters) to keep `git bisect` useful.
- Per Constitution Principle VI: do not retain the deleted `WebRoutingShellResolver` helpers as `[Obsolete]` shims. Phase 9 verifies clean removal.
