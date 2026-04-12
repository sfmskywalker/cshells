# Tasks: Fluent Assembly Source Selection

**Input**: Design documents from `/specs/003-fluent-assembly-selection/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `quickstart.md`, `contracts/feature-assembly-provider-contract.md`, `contracts/naming-decision-record.md`

**Tests**: Include focused unit and integration tests because the specification, research, quickstart, and constitution explicitly require coverage for fluent builder composition, explicit/default host behavior, custom provider extensibility, deduplicated discovery, and breaking API changes.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently once the shared discovery infrastructure is in place.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the new public feature-discovery contract that the rest of the implementation will build on.

- [X] T001 Create the public `IFeatureAssemblyProvider` contract with XML documentation in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the shared builder and discovery infrastructure that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Extend `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilder.cs` with an ordered `_featureAssemblyProviderRegistrations` list, explicit-mode detection, and provider-construction helpers
- [X] T003 [P] Extract host/default assembly resolution into `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs` and add the built-in host provider in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/HostFeatureAssemblyProvider.cs`
- [X] T004 [P] Add the built-in explicit provider in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/ExplicitFeatureAssemblyProvider.cs` so explicit assembly contributions can be represented independently of the fluent API surface
- [X] T005 Wire implicit-host-default versus explicit-provider discovery selection into `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/ServiceCollectionExtensions.cs`

**Checkpoint**: The public provider abstraction exists, the builder can retain ordered provider registrations, and core discovery can switch cleanly between implicit host-default and explicit-provider modes.

---

## Phase 3: User Story 1 - Configure Feature Discovery Fluently (Priority: P1) 🎯 MVP

**Goal**: Let application developers configure feature discovery only through fluent builder calls while preserving additive composition across explicit assembly-source calls.

**Independent Test**: Configure shells with `FromAssemblies(...)` calls only, verify expected features are discovered without trailing assembly arguments, and confirm each fluent call appends another provider contribution instead of replacing earlier ones.

### Tests for User Story 1

> **NOTE**: Write these tests first, confirm they fail against the current code, then implement the fluent assembly-source behavior.

- [X] T006 [P] [US1] Add append-order, empty-input, and null-explicit-assembly guard coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs`
- [X] T007 [P] [US1] Add explicit-provider aggregation and duplicate-assembly deduplication coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/FeatureAssemblyProviderTests.cs`

### Implementation for User Story 1

- [X] T008 [US1] Implement fluent `FromAssemblies(params Assembly[] assemblies)` registration in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`
- [X] T009 [US1] Remove legacy assembly-argument support from `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/ServiceCollectionExtensions.cs` so `AddCShells(...)` relies only on builder-managed provider configuration
- [X] T010 [US1] Finalize explicit assembly aggregation, deduplicated first-seen ordering, and zero-contribution explicit-mode handling in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilder.cs`

**Checkpoint**: Fluent explicit-assembly configuration works end to end, additive calls are retained in order, duplicate assemblies are scanned once, and the core API no longer advertises trailing assembly arguments.

---

## Phase 4: User Story 2 - Explicitly Include Host Assemblies (Priority: P2)

**Goal**: Let developers explicitly opt host-derived assemblies back into discovery while preserving the existing implicit default behavior when no assembly-source calls are made.

**Independent Test**: Compare a default configuration with no assembly-source calls against a configuration that explicitly uses `FromHostAssemblies()` and verify the discovered host feature set is equivalent; then confirm explicit custom/explicit sources do not include host assemblies unless `FromHostAssemblies()` is appended.

### Tests for User Story 2

- [X] T011 [P] [US2] Add default-versus-`FromHostAssemblies()` equivalence and explicit-mode host-exclusion coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs`
- [X] T012 [P] [US2] Add repeated-host-provider deduplication and host-resolution equivalence coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/HostFeatureAssemblyProviderTests.cs`

### Implementation for User Story 2

- [X] T013 [US2] Implement fluent `FromHostAssemblies()` by reusing the shared host-resolution algorithm in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/HostFeatureAssemblyProvider.cs`
- [X] T014 [US2] Remove legacy assembly-argument overloads and preserve fluent-only default-host behavior in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ShellExtensions.cs`

**Checkpoint**: Default host-derived discovery remains unchanged when no source methods are called, `FromHostAssemblies()` explicitly restores the same host-derived set in explicit mode, and ASP.NET Core entry points no longer expose legacy assembly-argument overloads.

---

## Phase 5: User Story 3 - Extend Discovery with Custom Providers (Priority: P3)

**Goal**: Let developers append their own `IFeatureAssemblyProvider` implementations through the public builder API so custom discovery sources participate additively with built-in providers.

**Independent Test**: Register custom providers through the public builder API and verify they contribute assemblies additively, preserve provider-registration order, and still produce a deduplicated discovery set when combined with built-in providers.

### Tests for User Story 3

- [X] T015 [P] [US3] Add generic, instance, and factory `WithAssemblyProvider(...)` coverage with null-guard assertions, root-DI generic resolution behavior, and non-null provider-result validation in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs`
- [X] T016 [P] [US3] Add custom-provider additive composition, root-service-provider context propagation, and deduplicated discovery coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs`

### Implementation for User Story 3

- [X] T017 [US3] Implement public `WithAssemblyProvider<TProvider>()`, instance, and factory overloads in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`, including root-DI resolution and fail-fast behavior for unresolved generic providers
- [X] T018 [US3] Ensure custom provider registrations are retained, materialized, and aggregated additively in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilder.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs`

**Checkpoint**: The public custom-provider extension point is live, built-in and custom providers compose in one ordered list, and discovery stays deduplicated regardless of provider overlap.

---

## Phase 6: User Story 4 - Adopt Clear Naming for the New API Surface (Priority: P4)

**Goal**: Apply the approved naming set consistently across the public API surface, internal implementation seams, and entry-point XML docs.

**Independent Test**: Review the final public API and XML docs to confirm the approved names are used consistently, rejected alternatives are absent from active guidance, and the custom-provider entry point matches the naming decision record.

### Implementation for User Story 4

- [X] T019 [P] [US4] Apply the approved naming set and required XML docs for all new public fluent builder methods and overloads across `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilder.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/HostFeatureAssemblyProvider.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/ExplicitFeatureAssemblyProvider.cs`
- [X] T020 [P] [US4] Remove rejected or legacy naming from entry-point XML docs in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/ServiceCollectionExtensions.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ShellExtensions.cs`

**Checkpoint**: The codebase uses one approved naming set for the provider abstraction, fluent methods, built-in provider types, and supporting builder/resolver members.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Update public guidance, migrate in-repo examples, and validate the completed feature against the quickstart flow.

- [X] T021 [P] Update fluent assembly-selection and migration guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/README.md` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/README.md`
- [X] T022 [P] Update additive provider composition and explicit/default host behavior guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/getting-started.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/integration-patterns.md`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/multiple-shell-providers.md`
- [X] T023 [P] Update mirrored guidance and runnable examples in `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Getting-Started.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Creating-Features.md`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/Program.cs`
- [X] T024 Run the quickstart validation flow from `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/003-fluent-assembly-selection/quickstart.md` and the regression suite rooted at `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/`
- [X] T025 [US4] Audit the final public API, XML docs, examples, and `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/003-fluent-assembly-selection/contracts/naming-decision-record.md` for approved-name consistency and rejected-name removal across the repo

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1: Setup**: No dependencies; start immediately.
- **Phase 2: Foundational**: Depends on Phase 1 and blocks all user stories.
- **Phase 3: User Story 1 (P1)**: Depends on Foundational completion; delivers the MVP fluent explicit-assembly path.
- **Phase 4: User Story 2 (P2)**: Depends on Foundational completion; shares builder-extension and ASP.NET Core entry-point files with User Story 1, so coordinate edits if parallel work is attempted.
- **Phase 5: User Story 3 (P3)**: Depends on Foundational completion; should follow the builder/provider infrastructure established by User Story 1 even though its behavior remains independently testable.
- **Phase 6: User Story 4 (P4)**: Depends on the new API surface existing; complete after User Stories 1-3 have stabilized the names that must be applied consistently.
- **Phase 7: Polish**: Depends on the stories you plan to ship; run after the public API and tests are stable.

### User Story Dependencies

- **User Story 1 (P1)**: First deliverable and MVP; no dependency on later stories.
- **User Story 2 (P2)**: Independent in behavior after Foundational, but it edits some of the same registration files as User Story 1.
- **User Story 3 (P3)**: Independent in behavior after Foundational, but it extends the same builder/provider flow introduced in User Story 1.
- **User Story 4 (P4)**: Depends on the finalized API shape from User Stories 1-3 so naming can be applied consistently without churn.

### Within Each User Story

- Test tasks should be completed first and confirmed failing before implementation is considered done.
- Builder/provider registration changes precede service-registration entry-point cleanup.
- Discovery aggregation changes precede documentation and sample updates.
- Legacy overload removal precedes public documentation migration so examples only show supported APIs.

### Parallel Opportunities

- T003 and T004 can run in parallel after T002.
- T006 and T007 can run in parallel for User Story 1.
- T011 and T012 can run in parallel for User Story 2.
- T015 and T016 can run in parallel for User Story 3.
- T019 and T020 can run in parallel for User Story 4.
- T021, T022, and T023 can run in parallel during Polish.
- T025 should run after T019-T023 so the naming audit verifies the final state.

---

## Parallel Example: User Story 1

```bash
# Launch User Story 1 test work in parallel:
Task: "Add append-order, empty-input, and null-explicit-assembly guard coverage in tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs"
Task: "Add explicit-provider aggregation and duplicate-assembly deduplication coverage in tests/CShells.Tests/Unit/Features/FeatureAssemblyProviderTests.cs"
```

## Parallel Example: User Story 2

```bash
# Launch User Story 2 verification work in parallel:
Task: "Add default-versus-FromHostAssemblies() equivalence and explicit-mode host-exclusion coverage in tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs"
Task: "Add repeated-host-provider deduplication and host-resolution equivalence coverage in tests/CShells.Tests/Unit/Features/HostFeatureAssemblyProviderTests.cs"
```

## Parallel Example: User Story 3

```bash
# Launch User Story 3 verification work in parallel:
Task: "Add generic, instance, and factory WithAssemblyProvider(...) coverage with null-guard assertions in tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs"
Task: "Add custom-provider additive composition and deduplicated discovery coverage in tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs"
```

## Parallel Example: User Story 4

```bash
# Launch naming-alignment work in parallel once User Stories 1-3 stabilize the final API shape:
Task: "Apply the approved naming set across src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs, src/CShells/DependencyInjection/CShellsBuilder.cs, src/CShells/DependencyInjection/CShellsBuilderExtensions.cs, and src/CShells/Features/*.cs"
Task: "Remove rejected or legacy naming from entry-point XML docs in src/CShells/DependencyInjection/ServiceCollectionExtensions.cs, src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs, and src/CShells.AspNetCore/Extensions/ShellExtensions.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate fluent explicit-assembly configuration independently before moving on.

### Incremental Delivery

1. Finish Setup + Foundational so the new provider abstraction, ordered builder list, and resolver wiring are stable.
2. Deliver User Story 1 to move explicit assembly selection onto the fluent builder and remove the core legacy API.
3. Deliver User Story 2 to reintroduce host assemblies explicitly and remove ASP.NET Core legacy overloads.
4. Deliver User Story 3 to open the public custom-provider extension point.
5. Deliver User Story 4 to lock naming consistency across the public surface.
6. Finish with docs, sample updates, and quickstart/regression validation.

### Parallel Team Strategy

1. One developer completes T002 while another prepares the new resolver/provider files for T003-T004.
2. After Foundational is complete, split unit and integration test tasks within each story across developers.
3. Reserve shared-file builder-extension changes for coordinated work because User Stories 1-3 all touch `/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`.
4. Once the API is stable, documentation and sample updates can proceed in parallel with final validation.

---

## Notes

- `[P]` tasks touch different files and can be run in parallel safely.
- `[US1]`, `[US2]`, `[US3]`, and `[US4]` map directly to the prioritized stories in the feature spec.
- The task order intentionally reflects the finalized design: public `IFeatureAssemblyProvider`, builder-managed ordered provider list, built-in host and explicit providers, public custom-provider entry point, additive composition semantics, explicit-mode/default-host behavior, legacy API removal, tests, docs, and naming consistency.
- Keep the implementation focused on the listed files; avoid introducing alternative assembly-selection models or compatibility shims for removed overloads.
