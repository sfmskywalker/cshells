# Tasks: Polymorphic Feature Configuration

**Input**: Design documents from `/specs/014-polymorphic-feature-config/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/feature-configuration.md, quickstart.md

**Tests**: Test tasks are included because the feature specification defines measurable test outcomes for each value form, precedence rule, and invalid case.

**Organization**: Tasks are grouped by user story so compact enablement, disablement, re-enablement, and direct settings compatibility can be implemented and validated as independent increments.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the working baseline and locate affected code paths.

- [x] T001 Run baseline configuration tests with `dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~Configuration"` from repository root
- [x] T002 [P] Review existing feature configuration behavior in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T003 [P] Review current shell merge behavior in `src/CShells/Lifecycle/Providers/ConfiguredShellBlueprintProvider.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared declaration state and merge helpers required by all user stories.

**CRITICAL**: No user story implementation should begin until this phase is complete.

- [x] T004 Add explicit enablement state, reset semantics, and XML docs to `src/CShells/Configuration/FeatureEntry.cs`
- [x] T005 Add explicit disabled feature tracking with XML docs to `src/CShells.Abstractions/ShellSettings.cs`
- [x] T006 Implement reusable feature add/remove/reset helper methods in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T007 Update `src/CShells/Configuration/ShellBuilder.cs` to use the shared helper methods for deduplicated feature additions
- [x] T008 Update `src/CShells/Lifecycle/Blueprints/ConfigurationShellBlueprint.cs` to compose settings through normalized feature declarations

**Checkpoint**: Feature declarations can represent enabled and disabled intent before story-specific parsing and merge behavior is added.

---

## Phase 3: User Story 1 - Enable Features With Compact Values (Priority: P1) MVP

**Goal**: Enable features with `true`, string `"true"`, `{}`, and object values while preserving direct feature settings.

**Independent Test**: Load shell configuration with `true`, `"true"`, `{}`, and object feature entries and confirm all requested features are enabled with expected settings.

### Tests for User Story 1

- [x] T009 [P] [US1] Add JSON converter tests for `true`, string `"true"`, `{}`, and object feature entries in `tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs`
- [x] T010 [P] [US1] Add configuration blueprint tests for compact `true` and string `"true"` feature entries in `tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs`

### Implementation for User Story 1

- [x] T011 [US1] Extend object-map parsing for boolean `true` and string `"true"` values in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T012 [US1] Extend object-map JSON deserialization and serialization for enabled declarations in `src/CShells/Configuration/FeatureEntryListJsonConverter.cs`
- [x] T013 [US1] Preserve empty object and configured object feature behavior while using declaration state in `src/CShells/Configuration/FeatureEntryListJsonConverter.cs`
- [x] T014 [US1] Update ShellConfig feature format documentation comments for compact values in `src/CShells/Configuration/ShellConfig.cs`
- [x] T015 [US1] Run US1 focused tests with `dotnet test tests/CShells.Tests/ --filter "ShellConfigJsonConverterTests|ConfigurationShellBlueprintTests"` from repository root

**Checkpoint**: User Story 1 works independently as the MVP: compact enablement is supported without changing feature settings paths.

---

## Phase 4: User Story 2 - Disable Default Features From Later Configuration (Priority: P2)

**Goal**: Allow later configuration to disable features from lower-priority configuration or code-first defaults, including environment-style string `"false"` and unknown-feature no-op disablements.

**Independent Test**: Load defaults plus higher-priority overrides that set features to `false` or `"false"` and confirm disabled features are absent from activation while unrelated features remain enabled.

### Tests for User Story 2

- [x] T016 [P] [US2] Add parser tests for native `false`, string `"false"`, unsupported scalars, and `null` in `tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs`
- [x] T017 [P] [US2] Add blueprint tests for disabling configured features and preserving unrelated features in `tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs`
- [x] T018 [P] [US2] Add code-first default disablement tests in `tests/CShells.Tests/Unit/ShellBuilderTests.cs`
- [x] T019 [P] [US2] Add unknown disabled feature no-op and unknown positive missing-feature diagnostics tests in `tests/CShells.Tests/Integration/FeatureDependency/UnknownFeatureDependencyTests.cs`

### Implementation for User Story 2

- [x] T020 [US2] Extend object-map parsing for boolean `false` and string `"false"` values in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T021 [US2] Reject `null`, unsupported scalar values, and array feature values with actionable path messages in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T022 [US2] Apply disabled feature declarations when `ShellBuilder.FromConfiguration(IConfigurationSection)` merges into existing settings in `src/CShells/Configuration/ShellBuilder.cs`
- [x] T023 [US2] Apply disabled feature declarations when `ShellBuilder.FromConfiguration(ShellConfig)` merges into existing settings in `src/CShells/Configuration/ShellBuilder.cs`
- [x] T024 [US2] Update code-first default merging to let shell-specific disabled declarations remove defaults in `src/CShells/Lifecycle/Providers/ConfiguredShellBlueprintProvider.cs`
- [x] T025 [US2] Warn for unknown positive feature declarations while ignoring unknown disabled declarations before activation in `src/CShells/Lifecycle/ShellProviderBuilder.cs`
- [x] T026 [US2] Run US2 focused tests with `dotnet test tests/CShells.Tests/ --filter "ShellBuilderTests|ConfigurationShellBlueprintTests|UnknownFeatureDependencyTests"` from repository root

**Checkpoint**: User Stories 1 and 2 both work independently; deployment overrides can disable defaults.

---

## Phase 5: User Story 3 - Re-Enable Or Reconfigure Features From Later Configuration (Priority: P3)

**Goal**: Allow later `true` or object declarations to re-enable disabled features, with `true` resetting inherited settings and object entries merging settings normally.

**Independent Test**: Compose multiple configuration layers where earlier sources disable or configure a feature and later sources re-enable it with `true` or object values.

### Tests for User Story 3

- [x] T027 [P] [US3] Add layered configuration tests for `false` followed by `true` and `false` followed by object in `tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs`
- [x] T028 [P] [US3] Add reset-to-default tests proving higher-priority `true` drops lower-priority option keys in `tests/CShells.Tests/Unit/ShellBuilderTests.cs`
- [x] T029 [P] [US3] Add object merge precedence tests for feature settings in `tests/CShells.Tests/Unit/Configuration/ShellConfigurationTests.cs`

### Implementation for User Story 3

- [x] T030 [US3] Implement feature settings reset when a higher-priority enabled-with-defaults declaration is applied in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T031 [US3] Preserve normal object feature settings merging for higher-priority object declarations in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T032 [US3] Ensure shell-specific `true` re-enables code-first disabled state without inheriting default feature option keys in `src/CShells/Lifecycle/Providers/ConfiguredShellBlueprintProvider.cs`
- [x] T033 [US3] Run US3 focused tests with `dotnet test tests/CShells.Tests/ --filter "ConfigurationShellBlueprintTests|ShellBuilderTests|ShellConfigurationTests"` from repository root

**Checkpoint**: Re-enable and reconfigure precedence works independently across layered configuration.

---

## Phase 6: User Story 4 - Keep Feature Settings Direct And Familiar (Priority: P4)

**Goal**: Preserve direct feature option binding and ensure `Enabled` inside an object remains a feature setting, not control metadata.

**Independent Test**: Load object-based feature settings, including an inner `Enabled` property, and confirm options bind from the same paths as before without a `Settings` wrapper.

### Tests for User Story 4

- [x] T034 [P] [US4] Add direct `Enabled` setting tests in `tests/CShells.Tests/Unit/Configuration/FeatureEntryJsonConverterTests.cs`
- [x] T035 [P] [US4] Add direct object binding regression tests in `tests/CShells.Tests/Unit/Features/FeatureConfigurationBinderTests.cs`
- [x] T036 [P] [US4] Add sample configuration regression coverage for direct feature settings in `tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs`

### Implementation for User Story 4

- [x] T037 [US4] Ensure object-map child properties including `Enabled` are always treated as settings in `src/CShells/Configuration/ConfigurationHelper.cs`
- [x] T038 [US4] Ensure JSON object-map child properties including `Enabled` are always treated as settings in `src/CShells/Configuration/FeatureEntryListJsonConverter.cs`
- [x] T039 [US4] Remove or update stale `Settings` wrapper guidance from XML docs in `src/CShells/Configuration/FeatureEntryJsonConverter.cs`
- [x] T040 [US4] Run US4 focused tests with `dotnet test tests/CShells.Tests/ --filter "FeatureEntryJsonConverterTests|FeatureConfigurationBinderTests|ShellConfigJsonConverterTests"` from repository root

**Checkpoint**: Existing direct object feature settings remain familiar and backward-compatible.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, samples, cleanup, and full validation across all stories.

- [x] T041 [P] Update Workbench sample feature syntax to include compact `true`, `false`, and object examples in `samples/CShells.Workbench/appsettings.json`
- [x] T042 [P] Update public configuration documentation with polymorphic feature map examples in `README.md`
- [x] T043 [P] Update feature configuration documentation with Docker and environment override examples in `docs/feature-configuration.md`
- [x] T044 Remove obsolete array-first or `Settings` wrapper recommendations from `src/CShells/Configuration/ShellConfig.cs`
- [x] T045 Run quickstart validation commands from `specs/014-polymorphic-feature-config/quickstart.md`
- [x] T046 Run full test suite with `dotnet test CShells.sln` from repository root

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: Depends on Foundational and delivers the MVP compact enablement syntax.
- **US2 (Phase 4)**: Depends on Foundational; can start after US1 if sharing parser edits, but remains independently testable.
- **US3 (Phase 5)**: Depends on US2 because it builds on disable/re-enable precedence.
- **US4 (Phase 6)**: Depends on Foundational; can run after US1 because it validates object settings semantics.
- **Polish (Phase 7)**: Depends on all desired stories.

### User Story Dependencies

- **User Story 1 (P1)**: Foundational only.
- **User Story 2 (P2)**: Foundational only, but should be integrated after US1 to reduce parser conflicts.
- **User Story 3 (P3)**: Depends on US2 disablement semantics.
- **User Story 4 (P4)**: Foundational plus object parsing from US1.

### Parallel Opportunities

- T002 and T003 can run in parallel during setup.
- T009 and T010 can be written in parallel for US1.
- T016, T017, T018, and T019 can be written in parallel for US2.
- T027, T028, and T029 can be written in parallel for US3.
- T034, T035, and T036 can be written in parallel for US4.
- T041, T042, and T043 can be done in parallel during polish.

---

## Parallel Example: User Story 1

```text
Task: "T009 Add JSON converter tests for true, string true, {}, and object feature entries in tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs"
Task: "T010 Add configuration blueprint tests for compact true and string true feature entries in tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T016 Add parser tests for native false, string false, unsupported scalars, and null in tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs"
Task: "T018 Add code-first default disablement tests in tests/CShells.Tests/Unit/ShellBuilderTests.cs"
Task: "T019 Add unknown disabled feature no-op and unknown positive missing-feature diagnostics tests in tests/CShells.Tests/Integration/FeatureDependency/UnknownFeatureDependencyTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T027 Add layered configuration tests for false followed by true and false followed by object in tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs"
Task: "T029 Add object merge precedence tests for feature settings in tests/CShells.Tests/Unit/Configuration/ShellConfigurationTests.cs"
```

## Parallel Example: User Story 4

```text
Task: "T034 Add direct Enabled setting tests in tests/CShells.Tests/Unit/Configuration/FeatureEntryJsonConverterTests.cs"
Task: "T035 Add direct object binding regression tests in tests/CShells.Tests/Unit/Features/FeatureConfigurationBinderTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 for compact enablement with `true`, `"true"`, `{}`, and object values.
3. Run T015 and validate direct feature option settings still work.

### Incremental Delivery

1. US1: Compact enablement syntax.
2. US2: Disablement across layered configuration and code-first defaults.
3. US3: Re-enable and reset semantics.
4. US4: Direct settings compatibility and documentation polish.

### Validation Gates

- After each user story, run that story's focused test command.
- Before implementation is considered complete, run T045 and T046.

## Notes

- `[P]` tasks touch different files or are test-writing tasks that can be worked independently.
- Story labels map to the four user stories in `spec.md`.
- Tests should be written first and observed failing before implementation when practical.
- Keep public configuration syntax as `Features` map values; do not add public `EnabledFeatures`, `DisabledFeatures`, `Enabled`, or `Settings` configuration sections.
