# Tasks: Map-Based Shell Configuration

**Input**: Design documents from `/specs/011-map-shell-config/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/configuration-schema.md, quickstart.md

**Tests**: Test tasks are included because the specification explicitly requires verification for map loading, shell naming, environment overrides, layered merging, unsupported array rejection, and documentation/sample updates.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other tasks in the same phase because it touches different files or has no dependency on incomplete tasks
- **[Story]**: User story label for story phases only
- Every task includes an exact repository path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the current baseline and affected surface before changing behavior.

- [X] T001 Review existing shell configuration model and provider behavior in src/CShells/Configuration/CShellsOptions.cs, src/CShells/Configuration/ShellConfig.cs, and src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T002 [P] Review existing provider tests in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs for explicit Name fallback and array shell expectations
- [X] T003 [P] Review existing configuration model tests in tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs for shell-level Name and list-shaped CShellsOptions assumptions
- [X] T004 [P] Review sample and documentation shell-array references in README.md, docs/, wiki/, samples/CShells.Workbench/appsettings.json, src/CShells/README.md, src/CShells.AspNetCore/README.md, and src/CShells.FastEndpoints/README.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define shared model/provider rules that every story depends on.

**⚠️ CRITICAL**: No user story work can be completed until these shared behavior decisions are reflected in tests and code.

- [X] T005 Change CShellsOptions.Shells from list-shaped storage to map-shaped storage in src/CShells/Configuration/CShellsOptions.cs
- [X] T006 Remove shell identity requirements from ShellConfig and update XML docs to describe shell contents only in src/CShells/Configuration/ShellConfig.cs
- [X] T007 Update or replace shell configuration JSON model tests for map-shaped root options in tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs
- [X] T008 Add a shared test helper for building CShells:Shells configuration sections in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs

**Checkpoint**: Root configuration model and test scaffolding are ready for story-specific behavior.

---

## Phase 3: User Story 1 - Configure Shells By Name (Priority: P1) 🎯 MVP

**Goal**: Shells load from named map entries, and runtime shell identity always comes from the map key.

**Independent Test**: Load a map-format `CShells:Shells` section with multiple shell keys and verify provider get/list/compose operations expose the key-derived shell names and preserve configuration and feature settings.

### Tests for User Story 1

- [X] T009 [P] [US1] Add provider test for GetAsync using a map key without inner Name in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T010 [P] [US1] Add provider test for ListAsync returning map-key shell names in sorted order in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T011 [P] [US1] Add compose test proving map-key shell loads feature map settings and Configuration values in tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs
- [X] T012 [P] [US1] Add test proving an inner shell-level Name value does not override the map key in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs

### Implementation for User Story 1

- [X] T013 [US1] Remove explicit Name override fallback from GetAsync in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T014 [US1] Update ListAsync name resolution to return only validated child section keys in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T015 [US1] Replace ResolveShellName/TryResolveShellName helpers with a key-only validation helper in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T016 [US1] Update affected provider tests to expect key-derived names only in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T017 [US1] Run User Story 1 tests with dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~ConfigurationShellBlueprintProviderTests|FullyQualifiedName~ConfigurationShellBlueprintTests"

**Checkpoint**: User Story 1 is functional and testable independently: map-format shells load by name and do not use inner shell Name for identity.

---

## Phase 4: User Story 2 - Override Shell Settings With Stable Paths (Priority: P2)

**Goal**: Environment-style named paths target a specific shell and remain independent of shell ordering.

**Independent Test**: Load base shell config plus named override keys and verify only the targeted shell's feature setting changes.

### Tests for User Story 2

- [X] T018 [P] [US2] Add environment-style override test for CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY__SIGNINGKEY behavior in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T019 [P] [US2] Add test proving named override affects only Default when Default and Contoso both configure Identity in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T020 [P] [US2] Add test proving shell entry order does not affect named lookup or override targeting in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs

### Implementation for User Story 2

- [X] T021 [US2] Ensure ConfigurationShellBlueprintProvider.GetAsync directly resolves named shell sections after overrides in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T022 [US2] Ensure ConfigurationShellBlueprint ComposeAsync preserves feature map override values in src/CShells/Lifecycle/Blueprints/ConfigurationShellBlueprint.cs
- [X] T023 [US2] Run User Story 2 tests with dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~ConfigurationShellBlueprintProviderTests"

**Checkpoint**: User Story 2 is functional and testable independently: named environment-style paths target the intended shell only.

---

## Phase 5: User Story 3 - Merge Shell Configuration By Name (Priority: P3)

**Goal**: Layered configuration combines shell definitions by shell name, allowing overrides, extensions, and additions without array index coupling.

**Independent Test**: Load multiple configuration layers with named shell entries in different orders and verify the final shell set and settings merge by shell name.

### Tests for User Story 3

- [X] T024 [P] [US3] Add layered merge test where a later layer overrides Default:Configuration:Plan while preserving other Default values in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T025 [P] [US3] Add layered merge test where a later layer adds Contoso beside Default in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T026 [P] [US3] Add unsupported array syntax test for numeric CShells:Shells child keys in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs
- [X] T027 [P] [US3] Update integration tests that currently use CShells:Shells:0 paths to named shell paths in tests/CShells.Tests/Integration/Lifecycle/ShellRegistryConfigureAllShellsTests.cs

### Implementation for User Story 3

- [X] T028 [US3] Add numeric child-key rejection with an actionable CShells:Shells error in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T029 [US3] Ensure ListAsync fails before activation when numeric child keys indicate array shell syntax in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T030 [US3] Ensure GetAsync does not skip unrelated numeric array entries during lookup in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs
- [X] T031 [US3] Run User Story 3 tests with dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~ConfigurationShellBlueprintProviderTests|FullyQualifiedName~ShellRegistryConfigureAllShellsTests"

**Checkpoint**: User Story 3 is functional and testable independently: layered named configuration merges correctly and array shell syntax is rejected.

---

## Phase 6: User Story 4 - Update Guidance And Samples (Priority: P4)

**Goal**: Repository documentation and samples show only map-based shell configuration and named environment override paths.

**Independent Test**: Search documentation and samples for old `CShells:Shells` array examples, shell-level Name properties in shell examples, and index-based override paths; verify none remain except intentionally documented unsupported examples.

### Documentation Tasks for User Story 4

- [X] T032 [P] [US4] Update root shell configuration examples and env-var guidance in README.md
- [X] T033 [P] [US4] Update shell configuration guide examples in wiki/Configuring-Shells.md
- [X] T034 [P] [US4] Update getting started shell examples in docs/getting-started.md and wiki/Getting-Started.md
- [X] T035 [P] [US4] Update shell resolution examples in docs/shell-resolution.md and wiki/Shell-Resolution.md
- [X] T036 [P] [US4] Update integration pattern examples in docs/integration-patterns.md and wiki/Integration-Patterns.md
- [X] T037 [P] [US4] Update feature configuration precedence and override examples in docs/feature-configuration.md and wiki/Feature-Configuration.md
- [X] T038 [P] [US4] Update FastEndpoints shell examples in src/CShells.FastEndpoints/README.md and wiki/FastEndpoints-Integration.md
- [X] T039 [P] [US4] Update package README examples in src/CShells/README.md and src/CShells.AspNetCore/README.md
- [X] T040 [P] [US4] Convert sample app shell configuration to map syntax in samples/CShells.Workbench/appsettings.json
- [X] T041 [P] [US4] Review provider-specific standalone shell file examples in src/CShells.Providers.FluentStorage/README.md and update only references that describe CShells:Shells root syntax

### Validation for User Story 4

- [X] T042 [US4] Run documentation search to verify no unsupported CShells:Shells array examples remain in README.md, docs/, wiki/, samples/, and src/*/README.md
- [X] T043 [US4] Run documentation search to verify index-based shell override paths such as CShells:Shells:0 and CShells__Shells__0 are replaced in README.md, docs/, wiki/, samples/, and src/*/README.md

**Checkpoint**: User Story 4 is functional and testable independently: docs and samples teach the supported map syntax only.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validate the whole feature, clean up migration residue, and ensure quickstart scenarios pass.

- [X] T044 [P] Remove or update obsolete comments mentioning shell array support in src/CShells/Lifecycle/Providers/ConfigurationShellBlueprintProvider.cs and src/CShells/Lifecycle/Blueprints/ConfigurationShellBlueprint.cs
- [X] T045 [P] Search source and tests for CShells:Shells:0 paths and update any remaining shell-root usages to named paths in src/ and tests/
- [X] T046 [P] Search source and tests for shell-level Name assumptions in root shell configuration models and remove unsupported expectations in src/ and tests/
- [X] T047 Run quickstart verification commands from specs/011-map-shell-config/quickstart.md
- [X] T048 Run full validation with dotnet test and dotnet build
- [X] T049 Review git diff for unrelated changes and keep the final patch scoped to map-based shell configuration

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Setup; blocks user story completion.
- **User Story 1 (Phase 3)**: Depends on Foundational; establishes map-key shell identity and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational and benefits from US1 implementation; can be tested through named provider paths.
- **User Story 3 (Phase 5)**: Depends on Foundational and US1 rejection/name-resolution behavior.
- **User Story 4 (Phase 6)**: Depends on the contract decisions and can run in parallel with code stories after Foundational.
- **Polish (Phase 7)**: Depends on desired user stories being complete.

### User Story Dependencies

- **US1 Configure Shells By Name**: MVP; no dependency on other user stories after Foundational.
- **US2 Override Shell Settings With Stable Paths**: Requires map-key lookup from US1 for final behavior; tests can be drafted in parallel.
- **US3 Merge Shell Configuration By Name**: Requires map-key validation and array rejection semantics from US1.
- **US4 Update Guidance And Samples**: Can be implemented in parallel with code changes once the supported contract is stable.

### Within Each User Story

- Write/update tests first and confirm they fail where behavior changes.
- Implement the minimum code or documentation change for the story.
- Run the story-specific validation command before moving to the next story.
- Keep story changes independently reviewable.

### Parallel Opportunities

- T002, T003, and T004 can run in parallel during setup.
- T009, T010, T011, and T012 can be drafted in parallel because they cover separate acceptance cases.
- T018, T019, and T020 can be drafted in parallel because they are independent override scenarios.
- T024, T025, T026, and T027 can be drafted in parallel because they cover separate merge/rejection scenarios.
- T032 through T041 can run in parallel because they touch different documentation/sample files.
- T044, T045, and T046 can run in parallel after all implementation stories are complete.

---

## Parallel Example: User Story 1

```bash
Task: "Add provider test for GetAsync using a map key without inner Name in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs"
Task: "Add provider test for ListAsync returning map-key shell names in sorted order in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs"
Task: "Add compose test proving map-key shell loads feature map settings and Configuration values in tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs"
Task: "Add test proving an inner shell-level Name value does not override the map key in tests/CShells.Tests/Unit/Lifecycle/Providers/ConfigurationShellBlueprintProviderTests.cs"
```

## Parallel Example: User Story 4

```bash
Task: "Update root shell configuration examples and env-var guidance in README.md"
Task: "Update shell configuration guide examples in wiki/Configuring-Shells.md"
Task: "Update getting started shell examples in docs/getting-started.md and wiki/Getting-Started.md"
Task: "Convert sample app shell configuration to map syntax in samples/CShells.Workbench/appsettings.json"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Stop and validate with T017.
5. Demonstrate that `CShells:Shells:Default` loads a shell named `Default` without an inner `Name` property.

### Incremental Delivery

1. Deliver US1 to establish map-key shell identity.
2. Deliver US2 to prove named override paths work for operators.
3. Deliver US3 to prove layered configuration merges by name and old array syntax fails clearly.
4. Deliver US4 to align docs and samples with the only supported shape.
5. Complete Phase 7 validation before handoff.

### Parallel Team Strategy

1. One developer updates provider/model behavior for US1-US3.
2. Another developer updates docs and samples for US4 after the contract is confirmed.
3. Tests for US2 and US3 can be drafted while US1 implementation is in progress.
4. Final validation runs after code, docs, and samples converge.

## Notes

- `[P]` tasks must not edit the same file at the same time unless coordinated.
- Feature configuration array support is not in scope for removal; only shell array syntax under `CShells:Shells` is removed.
- Provider-specific standalone shell JSON files may still legitimately contain a shell-level `Name` if they are not examples of the `CShells:Shells` root map contract; review before changing.
- Keep error messages actionable and include `CShells:Shells` when rejecting old array syntax.
