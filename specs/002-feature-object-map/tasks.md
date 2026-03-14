# Tasks: Feature Object Map

**Input**: Design documents from `/specs/002-feature-object-map/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md, contracts/feature-configuration-contract.md

**Tests**: Include focused unit and integration tests because the specification, quickstart, and constitution require coverage for parsing, serialization, and validation behavior.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently once the shared foundation is complete.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Align the public configuration model with the planned dual-shape behavior before deeper parsing and serialization changes begin.

- [X] T001 Update dual-shape `Features` model documentation in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ShellConfig.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the shared parsing and serialization seams that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Implement shared `Features` shape detection and normalization helpers in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ConfigurationHelper.cs`
- [X] T003 [P] Extend collection-level JSON conversion for array/object-map input and preferred object-map output in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/FeatureEntryListJsonConverter.cs`
- [X] T004 [P] Align provider JSON options with the updated `ShellConfig` feature conversion path in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Providers.FluentStorage/FluentStorageShellSettingsProvider.cs`

**Checkpoint**: Shared feature-collection parsing and serialization seams are ready for story-specific behavior.

---

## Phase 3: User Story 1 - Configure Features Without Repeating Names (Priority: P1) 🎯 MVP

**Goal**: Allow shell authors to configure enabled features through an object map where the property key is the feature name and the property value supplies feature settings.

**Independent Test**: Load a shell definition whose `Features` value is an object map through `IConfiguration` and confirm enabled feature names, order, and flattened feature settings match the declared input.

### Tests for User Story 1

- [X] T005 [P] [US1] Add object-map `IConfiguration` binding scenarios in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/Configuration/ConfigurationBindingTests.cs`
- [X] T006 [P] [US1] Add object-map normalization and order-preservation coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs`
- [X] T007 [US1] Add coverage that an inner `Name` property in object-map syntax remains configuration while the map key remains feature identity in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs`

### Implementation for User Story 1

- [X] T008 [US1] Wire object-map parsing and map-key feature identity into `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ConfigurationHelper.cs`
- [X] T009 [US1] Apply normalized object-map feature collections across shell-building paths in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ShellSettingsFactory.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ShellBuilder.cs`

**Checkpoint**: Object-map `Features` definitions loaded through configuration providers work end to end and are independently testable.

---

## Phase 4: User Story 2 - Preserve Existing Array-Based Configurations (Priority: P2)

**Goal**: Keep array-based configurations working unchanged while supporting equivalent direct JSON deserialization and preferred object-map serialization for shell config models.

**Independent Test**: Deserialize equivalent array and object-map shell config JSON, serialize normalized shell configs back to JSON, and verify runtime feature names, settings, and preferred output shape remain correct.

### Tests for User Story 2

- [X] T010 [P] [US2] Add dual-shape list converter deserialization and serialization coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Configuration/FeatureEntryJsonConverterTests.cs`
- [X] T011 [P] [US2] Add `ShellConfig` object-map round-trip coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs`
- [X] T012 [P] [US2] Add duplicate configured feature rejection coverage for shell config JSON and runtime normalization in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs`

### Implementation for User Story 2

- [X] T013 [US2] Implement object-map deserialization, preferred object-map serialization, and duplicate configured feature rejection before serialization in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/FeatureEntryListJsonConverter.cs`
- [X] T014 [US2] Preserve array compatibility and direct JSON model equivalence in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/FeatureEntryJsonConverter.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ShellSettingsFactory.cs`

**Checkpoint**: Existing array configurations remain valid, direct JSON deserialization supports object-map syntax, and `ShellConfig` serialization prefers object-map output when lossless.

---

## Phase 5: User Story 3 - Receive Clear Feedback For Invalid Definitions (Priority: P3)

**Goal**: Reject invalid or ambiguous `Features` definitions with actionable errors instead of silently coercing them.

**Independent Test**: Supply invalid object-map JSON values and ambiguous mixed-shape configuration-provider input, then verify loading fails with errors that identify the affected shell and invalid feature entry or section.

### Tests for User Story 3

- [X] T015 [P] [US3] Add direct JSON invalid-entry error-context coverage, including shell name and feature entry details, in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs`
- [X] T016 [P] [US3] Add duplicate configured feature rejection coverage for `IConfiguration`-based shell loading in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/Configuration/ConfigurationBindingTests.cs`
- [X] T017 [US3] Add ambiguous mixed-shape configuration rejection coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/Configuration/ConfigurationBindingTests.cs`

### Implementation for User Story 3

- [X] T018 [US3] Implement shell-aware wrapping for invalid object-map JSON feature entries during `ShellConfig`-to-`ShellSettings` conversion in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ShellSettingsFactory.cs`
- [X] T019 [US3] Implement ambiguous-shape and duplicate configured feature name rejection with shell and section context in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ConfigurationHelper.cs`

**Checkpoint**: Invalid object-map and mixed-shape inputs fail clearly before shell activation and are independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Update user-facing examples and run the planned validation flow across the finished feature.

- [X] T020 [P] Update dual-shape feature configuration guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/feature-configuration.md`
- [X] T021 [P] Update wiki guidance for object-map feature configuration in `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Feature-Configuration.md`
- [X] T022 Update sample shell configuration examples in `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/appsettings.json`
- [X] T023 Run quickstart validation and focused test execution from `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/002-feature-object-map/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; start immediately.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational completion.
- **User Story 2 (Phase 4)**: Depends on Foundational completion and reuses the normalized feature collection behavior established for User Story 1.
- **User Story 3 (Phase 5)**: Depends on Foundational completion and validates the same parsing seams used by User Stories 1 and 2.
- **Polish (Phase 6)**: Depends on the user stories you intend to ship.

### User Story Dependencies

- **User Story 1 (P1)**: First deliverable and MVP; no dependency on later stories.
- **User Story 2 (P2)**: Builds on the shared dual-shape parsing boundary and validates compatibility/serialization behavior after object-map normalization exists.
- **User Story 3 (P3)**: Builds on the same parsing and serialization seams to add explicit rejection and error reporting.

### Within Each User Story

- Test tasks should be completed before or alongside implementation and must fail before the implementation is considered done.
- Shared parsing changes precede shell settings application changes.
- Serialization changes precede provider-specific validation and round-trip checks.
- Validation changes precede documentation and final quickstart verification.

### Parallel Opportunities

- T003 and T004 can run in parallel after T002.
- T005 and T006 can run in parallel for User Story 1; T007 follows T006 (same file).
- T010, T011, and T012 can run in parallel for User Story 2.
- T015 and T016 can run in parallel for User Story 3; T017 follows T016 (same file).
- T020 and T021 can run in parallel during Polish.

---

## Parallel Example: User Story 1

```bash
# Launch User Story 1 parallel work (T005 and T006 only):
Task: "Add object-map IConfiguration binding scenarios in tests/CShells.Tests/Integration/Configuration/ConfigurationBindingTests.cs"
Task: "Add object-map normalization and order-preservation coverage in tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs"
# T007 targets the same file as T006; run sequentially after T006:
Task: "Add coverage that an inner Name property in object-map syntax remains configuration while the map key remains feature identity in tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs"
```

## Parallel Example: User Story 2

```bash
# Launch User Story 2 test work in parallel:
Task: "Add dual-shape list converter deserialization and serialization coverage in tests/CShells.Tests/Unit/Configuration/FeatureEntryJsonConverterTests.cs"
Task: "Add ShellConfig object-map round-trip coverage in tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs"
Task: "Add duplicate configured feature rejection coverage for shell config JSON and runtime normalization in tests/CShells.Tests/Unit/Configuration/ShellConfigJsonConverterTests.cs and tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs"
```

## Parallel Example: User Story 3

```bash
# Launch User Story 3 parallel work (T015 and T016 only):
Task: "Add direct JSON invalid-entry error-context coverage, including shell name and feature entry details, in tests/CShells.Tests/Unit/ShellSettingsFactoryTests.cs"
Task: "Add duplicate configured feature rejection coverage for IConfiguration-based shell loading in tests/CShells.Tests/Integration/Configuration/ConfigurationBindingTests.cs"
# T017 targets the same file as T016; run sequentially after T016:
Task: "Add ambiguous mixed-shape configuration rejection coverage in tests/CShells.Tests/Integration/Configuration/ConfigurationBindingTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate object-map `IConfiguration` loading before moving on.

### Incremental Delivery

1. Finish Setup + Foundational so the shared parsing/serialization seam is stable.
2. Deliver User Story 1 to enable object-map input through configuration providers.
3. Deliver User Story 2 to complete compatibility, direct JSON support, and preferred serialization.
4. Deliver User Story 3 to finalize validation and actionable error reporting.
5. Finish with documentation, sample updates, and quickstart validation.

### Parallel Team Strategy

1. One developer completes T002 while another prepares T004.
2. After the foundation is ready, tests for each story can be split across unit and integration files.
3. Documentation updates can proceed in parallel once the user-facing syntax and validation messages are stable.

---

## Notes

- `[P]` tasks touch different files and can be run in parallel safely.
- `[US1]`, `[US2]`, and `[US3]` map directly to the prioritized stories in the feature spec.
- `T011` intentionally creates a focused `ShellConfig` JSON converter test file because the direct JSON contract is a separate user-facing behavior.
- `T012` and `FR-011` make duplicate configured feature names an explicit rejection case instead of a serialization fallback case.
- Keep tasks focused on the listed files; avoid opportunistic refactors outside the configuration pipeline.