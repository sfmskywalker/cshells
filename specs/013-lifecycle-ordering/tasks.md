# Tasks: Lifecycle Ordering

**Input**: Design documents from `/specs/013-lifecycle-ordering/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/lifecycle-ordering.md, quickstart.md

**Tests**: Test tasks are included because the feature specification explicitly requires coverage for dependency order versus initializer order, default compatibility, explicit order, diagnostics, drain compatibility, transient lifetime, and the Quartz-style scenario.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested as an independently valuable increment after the foundational lifecycle API is in place.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the planned files and test locations so later tasks have stable targets.

- [X] T001 Create lifecycle phase API file in src/CShells.Abstractions/Lifecycle/LifecyclePhase.cs
- [X] T002 [P] Create lifecycle order attribute file in src/CShells.Abstractions/Lifecycle/LifecycleOrderAttribute.cs
- [X] T003 [P] Create initializer registration metadata file in src/CShells.Abstractions/Lifecycle/ShellInitializerRegistration.cs
- [X] T004 [P] Create service collection lifecycle extension file in src/CShells.Abstractions/Lifecycle/ServiceCollectionLifecycleExtensions.cs
- [X] T005 [P] Create ordering planner test file in tests/CShells.Tests/Unit/Lifecycle/ShellInitializerOrderingPlannerTests.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the public lifecycle API and internal planning seam required before any user story can be completed.

**Critical**: No user story work should begin until these tasks compile because all stories depend on the registration metadata and planner seam.

- [X] T006 Define `LifecyclePhase` with ordered `Prepare`, `Default`, and `Start` values in src/CShells.Abstractions/Lifecycle/LifecyclePhase.cs
- [X] T007 [P] Define `LifecycleOrderAttribute` with phase/order metadata and XML docs in src/CShells.Abstractions/Lifecycle/LifecycleOrderAttribute.cs
- [X] T008 [P] Define `ShellInitializerRegistration` metadata record with initializer type, phase, order, registration index, explicit flag, source, and transient lifetime assumptions in src/CShells.Abstractions/Lifecycle/ShellInitializerRegistration.cs
- [X] T009 Implement `AddShellInitializer<TInitializer>()`, `AddShellInitializer<TInitializer>(int order)`, and `AddShellInitializer<TInitializer>(LifecyclePhase phase, int order)` overload signatures with guards in src/CShells.Abstractions/Lifecycle/ServiceCollectionLifecycleExtensions.cs
- [X] T010 Create `ShellInitializerOrderException` with actionable message support in src/CShells/Lifecycle/ShellInitializerOrderException.cs
- [X] T011 Create `ShellInitializerOrderingPlanner` skeleton that accepts initializer registrations and returns ordered entries in src/CShells/Lifecycle/ShellInitializerOrderingPlanner.cs
- [X] T012 Update lifecycle interface remarks to describe transient ordered registrations, `Default` compatibility, and first-class ordering in src/CShells.Abstractions/Lifecycle/IShellInitializer.cs
- [X] T013 Run a compile-focused check for new lifecycle API references using tests/CShells.Tests/Unit/Lifecycle/ShellInitializerOrderingPlannerTests.cs

**Checkpoint**: Public lifecycle ordering API and planner seam compile.

---

## Phase 3: User Story 1 - Order Initializers Independently Of Feature Dependencies (Priority: P1) MVP

**Goal**: A dependent feature can configure after its dependency while its initializer runs before the dependency's initializer.

**Independent Test**: Activate a shell with two features where Feature B depends on Feature A, verify Feature A configures first, Feature B's ordered initializer runs first, and initializer failures still abort activation.

### Tests for User Story 1

- [X] T014 [P] [US1] Add unit tests for phase/order sorting where `Prepare` entries run before `Default` and `Start` entries in tests/CShells.Tests/Unit/Lifecycle/ShellInitializerOrderingPlannerTests.cs
- [X] T015 [US1] Add integration test for feature dependency configuration order versus initializer execution order in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs
- [X] T016 [US1] Add integration test proving ordered initializer exceptions still abort activation and leave no partial shell entry in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs

### Implementation for User Story 1

- [X] T017 [US1] Implement phase-first then numeric-order sorting with stable registration-index tie-breaks in src/CShells/Lifecycle/ShellInitializerOrderingPlanner.cs
- [X] T018 [US1] Persist explicit initializer metadata from `AddShellInitializer` registrations in src/CShells.Abstractions/Lifecycle/ServiceCollectionLifecycleExtensions.cs
- [X] T019 [US1] Replace direct DI-order initializer enumeration with planner-based execution in src/CShells/Lifecycle/ShellRegistry.cs
- [X] T020 [US1] Ensure initializer execution still uses a shell provider async scope and passes cancellation through in src/CShells/Lifecycle/ShellRegistry.cs

**Checkpoint**: User Story 1 is independently functional and proves lifecycle order is separate from feature dependency order.

---

## Phase 4: User Story 2 - Keep Existing Initializer Behavior Compatible (Priority: P2)

**Goal**: Existing unordered `IShellInitializer` registrations run in `LifecyclePhase.Default` and keep their DI registration order by default.

**Independent Test**: Activate a shell with only legacy `AddTransient<IShellInitializer, T>()` registrations and verify the observed order matches current behavior.

### Tests for User Story 2

- [X] T021 [P] [US2] Add planner unit test for unordered initializers mapping to `LifecyclePhase.Default` while preserving registration order in tests/CShells.Tests/Unit/Lifecycle/ShellInitializerOrderingPlannerTests.cs
- [X] T022 [US2] Add integration regression test for legacy DI-order initializers in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs
- [X] T023 [US2] Add integration test proving `Prepare` ordered initializers run before legacy unordered initializers and `Start` initializers run after them in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs

### Implementation for User Story 2

- [X] T024 [US2] Add `Default` compatibility phase handling for unordered initializers in src/CShells/Lifecycle/ShellInitializerOrderingPlanner.cs
- [X] T025 [US2] Detect legacy `IShellInitializer` service descriptors without duplicating explicit registrations in src/CShells/Lifecycle/ShellInitializerOrderingPlanner.cs
- [X] T026 [US2] Preserve no-initializer activation fast path with planner integration in src/CShells/Lifecycle/ShellRegistry.cs
- [X] T027 [US2] Update compatibility guidance for legacy registrations in docs/shell-lifecycle.md

**Checkpoint**: User Story 2 is independently functional and existing initializer behavior remains compatible.

---

## Phase 5: User Story 3 - Offer Simple Authoring Options (Priority: P3)

**Goal**: Feature authors can use transient ordered registration overloads, attribute metadata, and semantic phases without manual descriptor replacement.

**Independent Test**: Register initializers through the supported authoring mechanisms and verify registration metadata overrides attribute metadata while all instances resolve from shell DI with transient lifetime.

### Tests for User Story 3

- [X] T028 [P] [US3] Add unit tests for `AddShellInitializer` overload validation, transient descriptor registration, and metadata capture in tests/CShells.Tests/Unit/Lifecycle/ShellInitializerOrderingPlannerTests.cs
- [X] T029 [US3] Add integration test for attribute metadata ordering through legacy initializer registration in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs
- [X] T030 [US3] Add integration test proving explicit registration metadata overrides `LifecycleOrderAttribute` metadata in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs
- [X] T031 [US3] Add integration test proving `AddShellInitializer<T>()` resolves a fresh transient initializer instance per activation in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs

### Implementation for User Story 3

- [X] T032 [US3] Implement attribute metadata discovery without constructing initializer instances in src/CShells/Lifecycle/ShellInitializerOrderingPlanner.cs
- [X] T033 [US3] Implement explicit registration metadata precedence over attribute metadata in src/CShells/Lifecycle/ShellInitializerOrderingPlanner.cs
- [X] T034 [US3] Ensure `AddShellInitializer` registers initializers as transient services without service descriptor replacement in src/CShells.Abstractions/Lifecycle/ServiceCollectionLifecycleExtensions.cs
- [X] T035 [US3] Add XML documentation examples for registration overloads, transient lifetime, phase ordering, and attribute usage in src/CShells.Abstractions/Lifecycle/ServiceCollectionLifecycleExtensions.cs

**Checkpoint**: User Story 3 is independently functional and feature authors have the documented first-class API.

---

## Phase 6: User Story 4 - Diagnose Ambiguous Lifecycle Ordering (Priority: P4)

**Goal**: Misconfigured lifecycle ordering fails or warns with actionable diagnostics before unsafe initializer side effects occur.

**Independent Test**: Configure invalid metadata, missing or mismatched lifecycle metadata, and equal phase/order ties; verify failures happen before initializer invocation and diagnostics identify affected types.

### Tests for User Story 4

- [X] T036 [P] [US4] Add planner unit tests for invalid initializer metadata and type mismatch diagnostics in tests/CShells.Tests/Unit/Lifecycle/ShellInitializerOrderingPlannerTests.cs
- [X] T037 [US4] Add integration test proving invalid lifecycle ordering fails before any initializer side effects are recorded in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs
- [X] T038 [US4] Add integration test for deterministic equal phase/order tie handling in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs

### Implementation for User Story 4

- [X] T039 [US4] Implement validation for invalid initializer metadata and non-assignable initializer types in src/CShells/Lifecycle/ShellInitializerOrderingPlanner.cs
- [X] T040 [US4] Include shell descriptor and initializer type names in ordering exception messages in src/CShells/Lifecycle/ShellInitializerOrderException.cs
- [X] T041 [US4] Log or expose non-fatal equal phase/order ambiguity diagnostics while preserving deterministic execution in src/CShells/Lifecycle/ShellRegistry.cs

**Checkpoint**: User Story 4 is independently functional and unsafe ordering failures are actionable.

---

## Phase 7: User Story 5 - Preserve Parallel Drain Behavior (Priority: P5)

**Goal**: Drain handlers keep the existing parallel behavior, and ordered or phased drain execution remains explicitly deferred.

**Independent Test**: Existing drain tests continue to prove handlers run in parallel and result/cancellation behavior is unchanged after initializer ordering is introduced.

### Tests for User Story 5

- [X] T042 [P] [US5] Add regression assertion that initializer lifecycle ordering does not affect parallel drain handlers in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryDrainTests.cs
- [X] T043 [US5] Add regression assertion for drain timeout and force behavior remaining unchanged in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryDrainTests.cs

### Implementation for User Story 5

- [X] T044 [US5] Keep `IDrainHandler` execution path parallel and unchanged while reviewing initializer-ordering changes in src/CShells/Lifecycle/DrainOperation.cs
- [X] T045 [US5] Update drain handler remarks to state current parallel behavior is preserved and ordered drain is deferred in src/CShells.Abstractions/Lifecycle/IDrainHandler.cs
- [X] T046 [US5] Document deferred ordered-drain design boundary in docs/shell-lifecycle.md

**Checkpoint**: User Story 5 is independently functional and existing drain parallelism remains intact.

---

## Phase 8: User Story 6 - Guide Provider And Base Feature Pairs (Priority: P6)

**Goal**: Documentation and a Quartz-style test show provider features depend on base features for configuration while ordering provider preparation before base startup.

**Independent Test**: Review documentation and run the Quartz-style activation test proving migrations complete before scheduler start while Quartz configuration still runs first.

### Tests for User Story 6

- [X] T047 [US6] Add Quartz-style provider/base integration test with configuration-order and initializer-order assertions in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs

### Implementation for User Story 6

- [X] T048 [US6] Add provider/base feature guidance and Quartz-style example in docs/integration-patterns.md
- [X] T049 [P] [US6] Add lifecycle ordering quick reference to docs/shell-lifecycle.md
- [X] T050 [P] [US6] Update package README lifecycle example to use transient `AddShellInitializer` with phases in src/CShells/README.md
- [X] T051 [P] [US6] Update abstractions README with lifecycle API summary in src/CShells.Abstractions/README.md

**Checkpoint**: User Story 6 is independently functional and provider/base guidance is visible in docs and package READMEs.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Validate, clean up, and make the feature ready for handoff.

- [X] T052 [P] Add root README note pointing lifecycle ordering readers to shell lifecycle documentation in README.md
- [X] T053 [P] Add wiki lifecycle ordering guidance mirroring docs for published docs sync in wiki/Shell-Lifecycle.md
- [X] T054 Run focused lifecycle tests from quickstart verification in specs/013-lifecycle-ordering/quickstart.md
- [X] T055 Run full repository test suite using developer workflow in AGENTS.md
- [X] T056 Review public XML documentation for all new lifecycle API members in src/CShells.Abstractions/Lifecycle/ServiceCollectionLifecycleExtensions.cs

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion; blocks every user story.
- **User Story 1 (Phase 3)**: Depends on Foundational; suggested MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational and should be validated after US1 wiring.
- **User Story 3 (Phase 5)**: Depends on Foundational; can proceed after US1 establishes runtime planner execution.
- **User Story 4 (Phase 6)**: Depends on US1 and US3 because diagnostics validate planner and metadata behavior.
- **User Story 5 (Phase 7)**: Depends on Foundational; can run in parallel with initializer-focused stories once drain tests are isolated.
- **User Story 6 (Phase 8)**: Depends on US1 and US3 because provider/base examples use the public ordering API.
- **Polish (Final Phase)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: MVP; establishes independent initializer order.
- **US2 (P2)**: Validates `Default` phase compatibility after US1 planner wiring.
- **US3 (P3)**: Adds full authoring ergonomics and transient lifetime guarantees after basic ordered execution exists.
- **US4 (P4)**: Adds diagnostics after metadata and planner behavior are present.
- **US5 (P5)**: Preserves drain behavior and can be validated independently after foundation.
- **US6 (P6)**: Adds provider/base guidance and Quartz-style coverage after API behavior exists.

### Parallel Opportunities

- Setup file creation tasks T002-T005 can run in parallel after T001.
- Foundational type-definition tasks T007-T008 can run in parallel with T010 once T006 exists.
- US1 test tasks T014-T016 can be drafted before implementation tasks T017-T020.
- US3 tests T028-T031 can be drafted in parallel because they target separate scenarios in the same test file but should be merged carefully.
- US5 drain tasks T042-T046 can proceed in parallel with documentation-heavy US6 tasks once foundation is complete.
- Final documentation tasks T052-T053 can run in parallel with XML documentation review T056.

---

## Parallel Example: User Story 1

```text
Task: "T014 [P] [US1] Add unit tests for phase/order sorting where `Prepare` entries run before `Default` and `Start` entries in tests/CShells.Tests/Unit/Lifecycle/ShellInitializerOrderingPlannerTests.cs"
Task: "T015 [US1] Add integration test for feature dependency configuration order versus initializer execution order in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs"
Task: "T016 [US1] Add integration test proving ordered initializer exceptions still abort activation and leave no partial shell entry in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs"
```

## Parallel Example: User Story 5

```text
Task: "T042 [P] [US5] Add regression assertion that initializer lifecycle ordering does not affect parallel drain handlers in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryDrainTests.cs"
Task: "T045 [US5] Update drain handler remarks to state current parallel behavior is preserved and ordered drain is deferred in src/CShells.Abstractions/Lifecycle/IDrainHandler.cs"
Task: "T046 [US5] Document deferred ordered-drain design boundary in docs/shell-lifecycle.md"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup files.
2. Complete Phase 2 foundational API and planner seam.
3. Complete Phase 3 User Story 1 tests and implementation.
4. Stop and validate with focused lifecycle tests for initializer ordering.

### Incremental Delivery

1. Deliver US1 to prove lifecycle order is independent from feature dependency order.
2. Deliver US2 to lock `Default` phase backward compatibility.
3. Deliver US3 to complete transient authoring APIs and attribute support.
4. Deliver US4 to make misconfiguration actionable.
5. Deliver US5 to preserve drain behavior.
6. Deliver US6 to document provider/base usage and the Quartz-style scenario.

### Validation

1. Run focused lifecycle tests after each story checkpoint.
2. Run `dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~Lifecycle"` after US5.
3. Run `dotnet test` before final handoff.
