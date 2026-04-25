---
description: "Task list for Single-Provider Blueprint Simplification — drop multi-provider composition from feature 007"
---

# Tasks: Single-Provider Blueprint Simplification

**Input**: Design documents from `/specs/008-single-provider-simplification/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Scope**: Deletion-heavy refactor. The composite provider, cursor codec,
composite-options class, and `DuplicateBlueprintException` are removed; the
registry's DI factory is rewritten to enforce a single-provider model with a
fail-fast composition-time guard. Public API surface that real consumers use is
unchanged in shape; the only behavioral change is that mixing `AddShell` with
`AddBlueprintProvider` becomes a startup error.

**Tests**: Included (Constitution Principle V). Existing 007 single-source tests
are carried forward and verified; composite-only tests are deleted; one new
test file covers the fail-fast guard (FR-005, FR-006) and the third-party
extensibility scenario (SC-008).

**Organization**: Phase 1 deletes obsolete code in lockstep with the DI
rewrite (must land atomically — partial deletion would break the build).
Phases 2–4 verify each priority-1 user story. Phase 5 covers the P2 migration
verification. Phase 6 is polish.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story this task belongs to (US1…US4). Setup, Foundational,
  and Polish phases have no story label.
- File paths are repo-root relative.

## Path Conventions

- **Abstractions**: `src/CShells.Abstractions/Lifecycle/`
- **Implementation**: `src/CShells/Lifecycle/`, `src/CShells/Lifecycle/Providers/`,
  `src/CShells/DependencyInjection/`
- **Tests**: `tests/CShells.Tests/Unit/Lifecycle/`,
  `tests/CShells.Tests/Integration/Lifecycle/`

---

## Phase 1: Foundational — Atomic Refactor

**Purpose**: delete the multi-provider composition machinery and rewrite the DI
factory to enforce single-provider semantics. These steps must land in a single
compile-clean unit because they have circular dependencies (deleting the
composite breaks the registry constructor; rewriting the registry constructor
breaks the DI factory; etc.).

**⚠️ CRITICAL**: After this phase, the build must be green even though
user-story-specific tests haven't been added yet.

### Source deletions

- [X] T001 [P] Delete `src/CShells.Abstractions/Lifecycle/DuplicateBlueprintException.cs` (FR-008).
- [X] T002 [P] Delete `src/CShells/Lifecycle/Providers/CompositeShellBlueprintProvider.cs` (FR-007). Also removes `CompositeProviderOptions` if it lives in the same file.
- [X] T003 [P] Delete `src/CShells/Lifecycle/Providers/CompositeCursorCodec.cs` and the `CompositeCursorEntry` record (FR-007).
- [X] T004 If `CompositeProviderOptions` was a separate file (`src/CShells/Lifecycle/Providers/CompositeProviderOptions.cs`), delete it. Verify zero remaining references before proceeding.

### Test deletions

- [X] T005 [P] Delete `tests/CShells.Tests/Unit/Lifecycle/Providers/CompositeCursorCodecTests.cs` (FR-010).
- [X] T006 [P] Delete `tests/CShells.Tests/Integration/Lifecycle/CompositeShellBlueprintProviderTests.cs` (FR-010).
- [X] T007 Modify `tests/CShells.Tests/Unit/Lifecycle/ExceptionMessageTests.cs`: drop the `Duplicate_NamesBothProviders` test method; keep `BlueprintNotMutable*` and `Unavailable_WrapsInner` (FR-010).

### Registry constructor change

- [X] T008 Modify `src/CShells/Lifecycle/ShellRegistry.cs`:
  - Change the public constructor's first parameter from `CompositeShellBlueprintProvider` to `IShellBlueprintProvider`.
  - Change the internal test-friendly ctor signature accordingly.
  - Simplify `ShouldWrapAsUnavailable` to drop the `DuplicateBlueprintException` exclusion (FR-009).
  - Remove the `using CShells.Lifecycle.Providers;` import if no longer needed.

### DI factory rewrite

- [X] T009 Modify `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs`:
  - Drop the `InMemoryShellBlueprintProvider` standalone DI registration.
  - Drop the `CompositeShellBlueprintProvider` DI registration entirely.
  - Replace the `IShellBlueprintProvider` registration with a single factory that branches per research R-003:
    - if `builder.ProviderFactories.Count > 1` → throw `InvalidOperationException` with the FR-006 message ("exactly one external provider is permitted; …");
    - if `builder.InlineBlueprints.Count > 0 && builder.ProviderFactories.Count == 1` → throw with the FR-005 teaching message (states the conflict; enumerates the three resolutions: move blueprints to the external source, drop the external provider, or implement a custom combining `IShellBlueprintProvider`);
    - if `builder.ProviderFactories.Count == 1` → return `builder.ProviderFactories[0](sp)`;
    - otherwise → return `new InMemoryShellBlueprintProvider(builder.InlineBlueprints)`.
  - Update the `ShellRegistry` factory to consume the single `IShellBlueprintProvider` resolved from DI.

### Builder verification

- [X] T010 Verify `src/CShells/DependencyInjection/CShellsBuilder.cs` does not need changes beyond what 007 provided. Confirm `_inlineBlueprints` and `_providerFactories` accumulate correctly; both `AddShell` and `AddBlueprintProvider` remain unchanged in shape (FR-012, FR-013).

### Migrate tests that depended on `InMemoryShellBlueprintProvider` being a DI service

The 007 tests dynamically add blueprints post-build by resolving
`InMemoryShellBlueprintProvider` from DI. With 008's design, the in-memory
provider is constructed inside the factory and not registered as a distinct
service. Each affected test moves to one of two patterns: (a) supply the
blueprint via the builder before host construction, or (b) register a
`StubShellBlueprintProvider` via `AddBlueprintProvider`.

- [X] T011 Modify `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryActivateTests.cs`:
  - Replace `host.GetRequiredService<InMemoryShellBlueprintProvider>().Add(new ThrowingBlueprint("payments"))` with builder-time registration: extend `BuildHost(...)` overload to accept an optional pre-built blueprint to add via `cshells.AddBlueprint(blueprint)` (or use `AddBlueprintProvider(_ => stub)` with a stub that vends the throwing blueprint).
  - Apply the same migration to the three affected test methods (`DuplicateBlueprint_Throws`, `CompositionException_Propagates_NoPartialEntry`, `Blueprint_NameMismatch_Throws`).
  - Drop the `using CShells.Lifecycle.Providers` import if it becomes unused.
- [X] T012 Modify `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryReloadTests.cs`:
  - Same migration for the `Reload_CompositionFailure_LeavesActiveUnchanged` test that resolves `InMemoryShellBlueprintProvider`.

### Build gate

- [X] T013 Run `dotnet build CShells.sln`. Confirm 0 warnings, 0 errors across `net8.0`, `net9.0`, `net10.0`. This validates that the deletion + rewrite landed cleanly together.

**Checkpoint**: Build is green. Existing single-source tests (US1, US2, US4
coverage) compile and run. Multi-source tests are gone. Composite types are
gone. The fail-fast guard is implemented but its behavior is not yet covered
by tests (added in Phase 4).

---

## Phase 2: User Story 1 — Code-only host (Priority: P1) ✅ Verification

**Goal**: confirm that the most common usage pattern (`AddShell` only) continues
to work identically after the simplification.

**Independent Test**: existing 007 integration test
`ShellRegistryGetOrActivateTests.GetOrActivate_CallsProviderOnce_Activates`
uses `AddBlueprintProvider` with a stub — that's the US2 path. Use
`ShellRegistryActivateTests.ActivateAsync_StampsGeneration1_AndPromotesToActive`
which uses `AddShell` (the US1 path) instead.

- [X] T014 [US1] Run the US1 carryover test:
  `dotnet test --filter "FullyQualifiedName~ShellRegistryActivateTests.ActivateAsync_StampsGeneration1"`.
  Confirm pass.
- [X] T015 [US1] Run the broader US1 carryover suite:
  `ShellRegistryActivateTests`, `ShellRegistryReloadTests`,
  `ShellRegistryReloadActiveTests`, `ShellRegistryPreWarmTests`,
  `ShellRegistryListTests` (single-source variants).
  Confirm all pass.

**Checkpoint**: code-only path is unchanged from a behavioral standpoint. SC-003
(zero code change for code-only hosts) is verified.

---

## Phase 3: User Story 2 — External-provider host (Priority: P1) ✅ Verification + new third-party test

**Goal**: confirm single-external-provider hosts still work; demonstrate the
extension seam is open by registering a custom third-party provider.

**Independent Test**: existing
`ShellRegistryGetOrActivateTests.GetOrActivate_CallsProviderOnce_Activates`
already exercises this path. New test in Phase 4 (`ShellRegistryGuardTests`)
adds the third-party-provider scenario for SC-008.

- [X] T016 [US2] Run the carryover suite for external providers:
  `ShellRegistryGetOrActivateTests`, `ConfigurationShellBlueprintProviderTests`,
  `FluentStorageShellBlueprintProviderTests` (if present in test suite).
  Confirm all pass.

**Checkpoint**: SC-004 (zero code change for single-external-provider hosts) is
verified for `WithConfigurationProvider` and `WithFluentStorageBlueprints`.
SC-008 is covered by the new test added in Phase 4.

---

## Phase 4: User Story 3 — Fail-fast guard (Priority: P1)

**Goal**: implement integration tests that exercise the FR-005 and FR-006
guards introduced in T009, plus the SC-008 third-party-provider scenario.

**Independent Test**: build a host that combines `AddShell` and
`AddBlueprintProvider`, assert that `services.BuildServiceProvider().GetRequiredService<IShellRegistry>()`
throws `InvalidOperationException` with a message that names both sources and
enumerates the three valid resolutions.

- [X] T017 [US3] Create `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryGuardTests.cs`. Cover:
  - `Mixing_AddShell_With_AddBlueprintProvider_ThrowsAtComposition_WithTeachingMessage` — asserts the exception type, that the message names `AddShell` and `AddBlueprintProvider`, that the message says "exactly one provider is permitted", and that the message enumerates the three resolutions (FR-005).
  - `MultipleExternalProviders_ThrowAtComposition` — calls `AddBlueprintProvider` twice with two different stubs; asserts the exception type and that the message states "exactly one external provider is permitted" (FR-006).
  - `Reverse_Order_AlsoThrows` — same as the first test but with `AddBlueprintProvider` registered before `AddShell`. Asserts that registration order does not matter (acceptance scenario 3.2).
  - `EmptyHost_NoProviderAndNoAddShell_ResolvesEmptyInMemory_NotFoundOnLookup` — confirms the "no provider configured" edge case is legal: registry resolves with an empty in-memory provider; `GetOrActivateAsync("anything")` raises `ShellBlueprintNotFoundException`.
  - `ThirdPartyCustomProvider_RegisteredViaAddBlueprintProvider_ActivatesShellsLikeShippedProviders` — registers a hand-rolled `ThirdPartyShellBlueprintProvider` (test-only class declared in the same file) via `AddBlueprintProvider`, activates a shell through it, asserts the activation succeeds identically to a shipped provider (SC-008).
- [X] T018 [US3] Run `ShellRegistryGuardTests`. Confirm all 5 tests pass.

**Checkpoint**: SC-005 (clear fail-fast on mixed-source) and SC-008 (third-party
extensibility) are verified.

---

## Phase 5: User Story 4 — Migration sweep (Priority: P2)

**Goal**: full carryover sweep — every test inherited from 007 that exercises a
single-source pattern continues to pass; the test count after deletion is at or
above 385 (SC-002).

- [X] T019 [US4] Run `dotnet test tests/CShells.Tests/CShells.Tests.csproj`. Verify pass count ≥ 385.
- [X] T020 [US4] Run `dotnet test tests/CShells.Tests.EndToEnd/CShells.Tests.EndToEnd.csproj`. Verify all 28 E2E tests pass (Workbench is single-source, so no migration needed).
- [X] T021 [US4] Verify SC-001 (production deletions): `git diff --stat HEAD~ -- src/` shows ≥ 350 lines deleted from production code. Verify SC-006 (public DI surface): `grep -rn "Composite\|DuplicateBlueprint" src/CShells.Abstractions/ src/CShells/ --include="*.cs"` returns zero matches.

**Checkpoint**: all four user stories satisfied. Test suite is at or above the
SC-002 floor. Production deletions match the SC-001 budget. Public surface is
clean per SC-006 and SC-007.

---

## Phase 6: Polish

**Purpose**: final quality pass.

- [X] T022 [P] Update XML doc-comments anywhere they reference the composite, the cursor codec, or `DuplicateBlueprintException` (FR-017). `grep -rn "Composite\|DuplicateBlueprint" src/ --include="*.cs"` — fix any remaining hits.
- [X] T023 [P] Spot-check `samples/CShells.Workbench/Program.cs` and `wiki/` for stale references. Workbench uses single-source so should be unchanged; wiki should be likewise clean.
- [X] T024 Final `dotnet build CShells.sln && dotnet test CShells.sln`. Confirm 0 warnings, 0 errors, all tests pass. This is the merge gate.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Foundational)**: no dependencies. Atomic — every task in Phase 1 must land before any subsequent phase runs because the build must be green.
- **Phase 2 (US1 verification)**: depends on Phase 1.
- **Phase 3 (US2 verification)**: depends on Phase 1. Independent of Phase 2.
- **Phase 4 (US3 guard tests)**: depends on Phase 1 (the guard is implemented in T009). Independent of Phases 2–3.
- **Phase 5 (US4 migration sweep)**: depends on Phases 1–4 being complete.
- **Phase 6 (Polish)**: depends on Phase 5.

### Cross-task file overlap (serialize)

- `src/CShells/Lifecycle/ShellRegistry.cs` — touched only by T008.
- `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs` — touched only by T009.
- `src/CShells/DependencyInjection/CShellsBuilder.cs` — touched only by T010 (likely no-op).
- `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryActivateTests.cs` — touched only by T011.
- `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryReloadTests.cs` — touched only by T012.

### Parallel Opportunities

- **T001–T007** (deletions + ExceptionMessageTests trim) — all on different files; can run in parallel.
- **T011–T012** (test migrations) — different files, parallel-safe.
- **T015 + T016** (carryover sweeps) — independent; can run as a single `dotnet test` if desired.
- **T022–T023** (polish doc updates) — different files, parallel-safe.

### Within Each Phase

- Phase 1's `T013` (build gate) MUST come last in the phase.
- Each verification phase (2, 3, 5) is just `dotnet test` invocations; serial execution.

---

## Implementation Strategy

### One-shot (recommended for this small feature)

The whole feature is small (~24 tasks, mostly deletions). Run all phases in
order in a single session, opening a PR at the end.

```
Phase 1 (T001–T013) → Phase 2 (T014–T015) → Phase 3 (T016)
  → Phase 4 (T017–T018) → Phase 5 (T019–T021) → Phase 6 (T022–T024)
```

### Single-developer sequencing notes

- Make the deletions first (T001–T007 in parallel).
- Then the registry change (T008) and DI factory rewrite (T009) — same person,
  same session, since they're tightly coupled.
- Migrate tests (T011–T012) once the build error tells you which references
  break.
- Build gate (T013).
- Verify carryovers (T014–T016).
- Add new guard tests (T017–T018).
- Sweep + polish (T019–T024).

---

## Notes

- This feature has **no new public abstractions**. Every type that callers
  interact with already shipped in 007. Only deletions + a DI factory rewrite.
- Per Constitution Principle VI: do not retain the deleted types as
  `[Obsolete]` shims. Callers migrate cleanly because there are no production
  callers (CShells is preview-prerelease).
- The composition-time guard error message is the primary teaching surface for
  the "exactly one provider" constraint. T009 defines it; T017 enforces its
  shape via assertions. Both must agree.
