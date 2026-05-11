# Tasks: Pattern-Based Shared Assemblies

**Input**: Design documents from `/specs/012-pattern-shared-assemblies/`
**Prerequisites**: [plan.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/plan.md), [spec.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/spec.md), [research.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/research.md), [data-model.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/data-model.md), [contracts/shared-assemblies.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/contracts/shared-assemblies.md), [quickstart.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/quickstart.md)

**Tests**: Included because the specification defines independent tests and 100% matching/validation success criteria.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently after the foundational selector model exists.

## Phase 1: Setup

**Purpose**: Prepare the work area and test files without changing behavior.

- [X] T001 Review existing feature assembly resolver behavior in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs`
- [X] T002 [P] Create shared assembly pattern test file in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/SharedAssemblyPatternTests.cs`
- [X] T003 [P] Create shared assembly configuration test file in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Configuration/SharedAssemblyConfigurationTests.cs`
- [X] T004 [P] Create shared assembly builder test file in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderSharedAssemblyTests.cs`
- [X] T005 [P] Create shared assembly resolver test file in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs`

---

## Phase 2: Foundational

**Purpose**: Define shared contracts and internal selector primitives required by all stories.

**Critical**: No user story implementation should start until this phase is complete.

- [X] T006 [P] Add public selector kind enum in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Features/SharedAssemblySelectorKind.cs`
- [X] T007 [P] Add public match diagnostics record in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Features/SharedAssemblyMatch.cs`
- [X] T008 [P] Add public selector contract in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Features/ISharedAssemblySelector.cs`
- [X] T009 Add internal prefix/exact parser and matcher in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblyPattern.cs`
- [X] T010 Add internal selector representation with source metadata in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblySelector.cs`
- [X] T011 Add internal selector provider and deduplication support in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblySelectorProvider.cs`

**Checkpoint**: Foundation ready; exact, prefix-pattern, source metadata, and diagnostics types exist.

---

## Phase 3: User Story 1 - Configure Shared Assemblies With Patterns (Priority: P1) MVP

**Goal**: A host can declare exact names and prefix wildcard patterns in root `CShells:SharedAssemblies`.

**Independent Test**: Load root configuration with `Elsa` and `Elsa.*`, then verify only `Elsa` and `Elsa.Workflows` are selected while `Contoso.Workflows` is not.

### Tests for User Story 1

- [X] T012 [P] [US1] Add configuration binding tests for root `CShells:SharedAssemblies` in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Configuration/SharedAssemblyConfigurationTests.cs`
- [X] T013 [P] [US1] Add exact-name and prefix-pattern matcher tests in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/SharedAssemblyPatternTests.cs`
- [X] T014 [P] [US1] Add resolver filtering tests for configured `Elsa` and `Elsa.*` selectors in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs`

### Implementation for User Story 1

- [X] T015 [US1] Add `SharedAssemblies` root option to `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/CShellsOptions.cs`
- [X] T016 [US1] Load root `CShells:SharedAssemblies` entries in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`
- [X] T017 [US1] Store configuration selectors on the builder in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilder.cs`
- [X] T018 [US1] Apply shared assembly selectors when resolving host assemblies in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs`
- [X] T019 [US1] Wire builder selector filtering into feature assembly resolution in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilder.cs`

**Checkpoint**: User Story 1 is independently functional and testable as the MVP.

---

## Phase 4: User Story 2 - Configure Shared Assemblies In Code (Priority: P2)

**Goal**: Library integrators can declare exact names, prefix wildcard patterns, and predicate selectors through code-first builder APIs.

**Independent Test**: Configure `WithSharedAssemblies("Elsa.*")` and `WithSharedAssembliesWhere(...)`, then verify included and excluded assemblies by simple name.

### Tests for User Story 2

- [X] T020 [P] [US2] Add `WithSharedAssemblies` exact and prefix-pattern API tests in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderSharedAssemblyTests.cs`
- [X] T021 [US2] Add `WithSharedAssembliesWhere` predicate include/exclude tests in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderSharedAssemblyTests.cs`
- [X] T022 [P] [US2] Add configuration plus code-first deduplication tests in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs`

### Implementation for User Story 2

- [X] T023 [US2] Add public `WithSharedAssemblies(params string[] patterns)` API in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`
- [X] T024 [US2] Add public `WithSharedAssembliesWhere(Func<string, bool> predicate)` API in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`
- [X] T025 [US2] Register code-first selector sources and predicate wrappers in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilder.cs`
- [X] T026 [US2] Evaluate predicate selectors against assembly simple names in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblySelectorProvider.cs`
- [X] T027 [US2] Deduplicate configuration and code-first matches by simple name in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblySelectorProvider.cs`

**Checkpoint**: User Story 2 works independently with code-first selectors and continues to compose with User Story 1.

---

## Phase 5: User Story 3 - Keep Matching Explicit And Bounded (Priority: P3)

**Goal**: Matching is limited to assembly simple names, invalid patterns fail early, and users can inspect which selector selected an assembly.

**Independent Test**: Verify full names, versions, cultures, public key tokens, file paths, and non-final `*` positions never affect matching; diagnostics identify the responsible selector source.

### Tests for User Story 3

- [X] T028 [P] [US3] Add invalid wildcard grammar tests for `*.Contracts` and `Elsa.*.Abstractions` in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/SharedAssemblyPatternTests.cs`
- [X] T029 [P] [US3] Add blank and whitespace selector validation tests in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Configuration/SharedAssemblyConfigurationTests.cs`
- [X] T030 [P] [US3] Add simple-name-only matching tests for full name and path-like candidates in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs`
- [X] T031 [P] [US3] Add predicate exception source feedback tests in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderSharedAssemblyTests.cs`
- [X] T032 [US3] Add match diagnostics source tests in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs`

### Implementation for User Story 3

- [X] T033 [US3] Enforce blank selector and non-final `*` validation messages in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblyPattern.cs`
- [X] T034 [US3] Include configuration paths and code-first source names in selector errors in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblySelectorProvider.cs`
- [X] T035 [US3] Ensure resolver extracts only `AssemblyName.Name` before matching in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs`
- [X] T036 [US3] Expose shared assembly match diagnostics from the selector provider in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblySelectorProvider.cs`
- [X] T037 [US3] Wrap predicate exceptions with actionable selector source feedback in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/SharedAssemblySelectorProvider.cs`

**Checkpoint**: User Story 3 confirms the isolation boundary and troubleshooting behavior.

---

## Phase 6: User Story 4 - Document Framework-Friendly Usage (Priority: P4)

**Goal**: Developers can find examples and guidance for exact names, prefix wildcard patterns, predicate selectors, and isolation tradeoffs.

**Independent Test**: Review docs and samples for root configuration, code-first examples, and broad-sharing cautions.

### Tests for User Story 4

- [X] T038 [P] [US4] Add documentation sample coverage checks in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Configuration/SharedAssemblyConfigurationTests.cs`

### Implementation for User Story 4

- [X] T039 [P] [US4] Add root `CShells:SharedAssemblies` example to `/Users/sipke/Projects/ValenceWorks/cshells/main/README.md`
- [X] T040 [P] [US4] Add shared assembly guidance to `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/getting-started.md`
- [X] T041 [P] [US4] Add framework integration guidance and isolation cautions to `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/integration-patterns.md`
- [X] T042 [P] [US4] Update Workbench sample configuration with a representative shared assembly example in `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/appsettings.json`
- [X] T043 [US4] Validate documented commands from quickstart in `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/quickstart.md`

**Checkpoint**: User Story 4 documentation is independently reviewable and matches implemented behavior.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency, cleanup, and full verification.

- [X] T044 [P] Run focused shared assembly tests with `dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~SharedAssembly"` from `/Users/sipke/Projects/ValenceWorks/cshells/main`
- [X] T045 Run full test suite with `dotnet test` from `/Users/sipke/Projects/ValenceWorks/cshells/main`
- [X] T046 [P] Review public XML documentation for new APIs in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Features/ISharedAssemblySelector.cs`
- [X] T047 [P] Review public XML documentation for builder APIs in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`
- [X] T048 Verify no package versions or new third-party dependencies were added in `/Users/sipke/Projects/ValenceWorks/cshells/main/Directory.Packages.props`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational; delivers MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational and can be implemented after or alongside US1, but final composition test depends on US1 selector loading.
- **User Story 3 (Phase 5)**: Depends on Foundational and can run alongside US1/US2 once matcher and selector provider exist.
- **User Story 4 (Phase 6)**: Depends on implemented API names from US1/US2 and final behavior from US3.
- **Polish (Phase 7)**: Depends on all desired user stories.

### User Story Dependencies

- **US1**: No dependency on other user stories; recommended MVP.
- **US2**: Independent code-first path, with one composition check against US1 configuration behavior.
- **US3**: Independent validation/diagnostics layer over the shared selector model.
- **US4**: Depends on settled public API and configuration shape.

### Parallel Opportunities

- T002-T005 can run in parallel because they create separate test files.
- T006-T008 can run in parallel because they create separate abstraction files.
- T012-T014 can run in parallel after T009-T011 because they target separate test concerns.
- T020-T022 can run in parallel after US1 foundation exists.
- T028-T032 can run in parallel because they cover separate validation and diagnostics behavior.
- T039-T042 can run in parallel once API names and configuration shape are stable.
- T044, T046, and T047 can run in parallel during polish after implementation.

## Parallel Example: User Story 1

```text
Task: "T012 [P] [US1] Add configuration binding tests for root CShells:SharedAssemblies in tests/CShells.Tests/Unit/Configuration/SharedAssemblyConfigurationTests.cs"
Task: "T013 [P] [US1] Add exact-name and prefix-pattern matcher tests in tests/CShells.Tests/Unit/Features/SharedAssemblyPatternTests.cs"
Task: "T014 [P] [US1] Add resolver filtering tests for configured Elsa and Elsa.* selectors in tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T020 [P] [US2] Add WithSharedAssemblies exact and prefix-pattern API tests in tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderSharedAssemblyTests.cs"
Task: "T022 [P] [US2] Add configuration plus code-first deduplication tests in tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T028 [P] [US3] Add invalid wildcard grammar tests in tests/CShells.Tests/Unit/Features/SharedAssemblyPatternTests.cs"
Task: "T029 [P] [US3] Add blank and whitespace selector validation tests in tests/CShells.Tests/Unit/Configuration/SharedAssemblyConfigurationTests.cs"
Task: "T030 [P] [US3] Add simple-name-only matching tests in tests/CShells.Tests/Unit/Features/FeatureAssemblyResolverSharedAssemblyTests.cs"
Task: "T031 [P] [US3] Add predicate exception source feedback tests in tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderSharedAssemblyTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 for User Story 1.
3. Run focused tests for configuration-driven exact and prefix-pattern matching.
4. Stop and validate that root `CShells:SharedAssemblies` works without code-first APIs.

### Incremental Delivery

1. Deliver US1 configuration-driven selectors as the MVP.
2. Add US2 code-first selectors and predicate support.
3. Add US3 diagnostics and bounded failure behavior.
4. Add US4 documentation and samples.
5. Run focused and full test suites.

### TDD Flow

1. For each story, write the story's test tasks first.
2. Confirm the new tests fail for the expected missing behavior.
3. Implement the story tasks.
4. Re-run the focused shared assembly test filter before moving to the next story.
