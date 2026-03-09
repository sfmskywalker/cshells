---

description: "Task list for implementing shell reload semantics"

---

# Tasks: Shell Reload Semantics

**Input**: Design documents from `/specs/001-shell-reload-semantics/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are included because the project constitution requires coverage for new functionality and the feature spec defines explicit independent test criteria.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- Public interfaces live under `src/CShells.Abstractions/`
- Core framework code lives under `src/CShells/`
- Unit tests live under `tests/CShells.Tests/Unit/`
- Integration tests live under `tests/CShells.Tests/Integration/`
- Runtime guidance lives in `docs/` and `wiki/`

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: Shared provider and host infrastructure required by all reload stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T001 Extend the provider contract with targeted lookup in src/CShells.Abstractions/Configuration/IShellSettingsProvider.cs, retire the duplicate public contract in src/CShells/Configuration/IShellSettingsProvider.cs, and update consumers to the abstractions assembly
- [X] T002 [P] Implement targeted lookup in src/CShells/Configuration/InMemoryShellSettingsProvider.cs and src/CShells/Configuration/MutableInMemoryShellSettingsProvider.cs
- [X] T003 [P] Implement targeted lookup in src/CShells/Configuration/ConfigurationShellSettingsProvider.cs and src/CShells/Configuration/CompositeShellSettingsProvider.cs
- [X] T004 [P] Implement targeted lookup in src/CShells.Providers.FluentStorage/FluentStorageShellSettingsProvider.cs
- [X] T005 [P] Add provider lookup unit coverage in tests/CShells.Tests/Unit/Configuration/ShellSettingsProviderLookupTests.cs
- [X] T006 Add internal host cache invalidation support in src/CShells/Hosting/DefaultShellHost.cs

**Checkpoint**: Provider lookup and host invalidation are ready; user story work can now begin in parallel

---

## Phase 2: User Story 1 - Reload One Known Shell (Priority: P1) 🎯 MVP

**Goal**: Add strict `ReloadShellAsync(ShellId)` semantics that refresh only the targeted shell and fail explicitly when the provider does not define it

**Independent Test**: Change one shell in a provider, call `ReloadShellAsync(shellId)`, and verify that the next access to that shell reflects the new configuration while unrelated shells remain unchanged; verify a missing provider result fails without mutating existing runtime state

### Tests for User Story 1

- [X] T007 [P] [US1] Add unit tests for strict targeted reload semantics in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs
- [X] T008 [P] [US1] Add integration tests for targeted shell rebuild behavior in tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs

### Implementation for User Story 1

- [X] T009 [US1] Extend the runtime management contract with `ReloadShellAsync` in src/CShells.Abstractions/Management/IShellManager.cs, retire the duplicate public contract in src/CShells/Management/IShellManager.cs, and update consumers to the abstractions assembly
- [X] T010 [US1] Implement strict targeted reload flow in src/CShells/Management/DefaultShellManager.cs
- [X] T011 [US1] Ensure targeted reload invalidates only the affected runtime shell context in src/CShells/Management/DefaultShellManager.cs and src/CShells/Hosting/DefaultShellHost.cs

**Checkpoint**: User Story 1 is fully functional and independently testable

---

## Phase 3: User Story 2 - Reload All Shells Without Stale Runtime State (Priority: P2)

**Goal**: Reconcile full runtime shell state to provider state and prevent stale `ShellContext` reuse after `ReloadAllShellsAsync()`

**Independent Test**: Activate shells, change provider-backed definitions, call `ReloadAllShellsAsync()`, and verify that changed shells rebuild from fresh settings, removed shells disappear, new shells become available, and unchanged shells remain usable

### Tests for User Story 2

- [X] T012 [P] [US2] Add unit tests for full-reload reconciliation behavior in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs
- [X] T013 [P] [US2] Add integration tests for stale-context invalidation after full reload in tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs

### Implementation for User Story 2

- [X] T014 [US2] Rework full reload reconciliation in src/CShells/Management/DefaultShellManager.cs
- [X] T015 [US2] Dispose and invalidate stale runtime contexts during reconciliation in src/CShells/Management/DefaultShellManager.cs and src/CShells/Hosting/DefaultShellHost.cs

**Checkpoint**: User Stories 1 and 2 both work independently, with full reload reconciling provider and runtime state correctly

---

## Phase 4: User Story 3 - Observe Reload Lifecycle Explicitly (Priority: P3)

**Goal**: Add explicit reload lifecycle notifications with deterministic ordering while preserving existing lifecycle notifications

**Independent Test**: Register notification observers, perform targeted and full reloads, and verify aggregate and per-shell reload notifications appear in the specified order around existing lifecycle events

### Tests for User Story 3

- [X] T016 [P] [US3] Add unit tests for reload notification ordering, changed-shell scope, and failure behavior in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs
- [X] T017 [P] [US3] Add integration tests for aggregate and per-shell reload notifications in tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs
- [X] T018 [P] [US3] Add explicit unit tests that existing lifecycle notifications are preserved during reload flows in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs
- [X] T019 [P] [US3] Add explicit integration tests that existing lifecycle notifications remain in the expected sequence around reload notifications in tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs

### Implementation for User Story 3

- [X] T020 [P] [US3] Add `ShellReloading` and `ShellReloaded` notification contracts in src/CShells/Notifications/ShellReloading.cs and src/CShells/Notifications/ShellReloaded.cs
- [X] T021 [US3] Emit deterministic single-shell reload notifications in src/CShells/Management/DefaultShellManager.cs
- [X] T022 [US3] Emit aggregate and per-shell full-reload notifications in src/CShells/Management/DefaultShellManager.cs while preserving the existing aggregate `ShellsReloaded` completion notification in src/CShells/Notifications/ShellsReloaded.cs

**Checkpoint**: All user stories are independently functional, including explicit reload lifecycle observability

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, regression validation, and cleanup across stories

- [X] T023 [P] Update runtime shell management guidance in wiki/Runtime-Shell-Management.md to cover strict targeted reload, full reconciliation, and notification ordering
- [X] T024 [P] Update multi-provider guidance in docs/multiple-shell-providers.md and wiki/Multiple-Shell-Providers.md to cover targeted provider lookup semantics, full-reload reconciliation behavior, and the notification behavior providers should expect during reload operations
- [X] T025 [P] Complete XML documentation for the relocated public contracts and new notification records in src/CShells.Abstractions/Configuration/IShellSettingsProvider.cs, src/CShells.Abstractions/Management/IShellManager.cs, src/CShells/Notifications/ShellReloading.cs, and src/CShells/Notifications/ShellReloaded.cs
- [X] T026 Run quickstart validation against specs/001-shell-reload-semantics/quickstart.md and capture any required doc adjustments in specs/001-shell-reload-semantics/quickstart.md
- [X] T027 Run full regression validation for reload changes against CShells.sln

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: No dependencies; blocks all user stories
- **User Story 1 (Phase 2)**: Depends on Foundational completion
- **User Story 2 (Phase 3)**: Depends on Foundational completion; may reuse US1 manager helpers but remains independently testable
- **User Story 3 (Phase 4)**: Depends on Foundational completion; builds on reload paths from US1 and US2 for notification coverage
- **Polish (Phase 5)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Starts after Foundational; MVP for targeted reload
- **User Story 2 (P2)**: Starts after Foundational; improves full reload semantics without blocking independent verification of US1
- **User Story 3 (P3)**: Starts after Foundational; depends on reload paths existing but can be tested separately through notification assertions

### Within Each User Story

- Write tests first and confirm they fail before implementation
- Extend public contracts before wiring implementations
- Invalidate runtime caches before relying on rebuilt contexts
- Finish story-specific validation before moving to the next priority

### Parallel Opportunities

- `T002`, `T003`, and `T004` can run in parallel after `T001`
- `T007` and `T008` can run in parallel for US1
- `T012` and `T013` can run in parallel for US2
- `T016` to `T019` can run in parallel for US3 test coverage
- `T023`, `T024`, and `T025` can run in parallel during polish

---

## Parallel Example: User Story 1

```bash
# Launch US1 tests together:
Task: "Add unit tests for strict targeted reload semantics in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs"
Task: "Add integration tests for targeted shell rebuild behavior in tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs"

# After tests are in place, implementation can split by contract and behavior:
Task: "Extend the runtime management contract with ReloadShellAsync in src/CShells.Abstractions/Management/IShellManager.cs"
Task: "Ensure targeted reload invalidates only the affected runtime shell context in src/CShells/Management/DefaultShellManager.cs and src/CShells/Hosting/DefaultShellHost.cs"
```

## Parallel Example: User Story 2

```bash
# Launch US2 validation together:
Task: "Add unit tests for full-reload reconciliation behavior in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs"
Task: "Add integration tests for stale-context invalidation after full reload in tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs"

# Split reconciliation and invalidation work:
Task: "Rework full reload reconciliation in src/CShells/Management/DefaultShellManager.cs"
Task: "Dispose and invalidate stale runtime contexts during reconciliation in src/CShells/Management/DefaultShellManager.cs and src/CShells/Hosting/DefaultShellHost.cs"
```

## Parallel Example: User Story 3

```bash
# Launch US3 tests together:
Task: "Add unit tests for reload notification ordering, changed-shell scope, and failure behavior in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs"
Task: "Add explicit integration tests that existing lifecycle notifications remain in the expected sequence around reload notifications in tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs"

# Split contracts and emission logic:
Task: "Add ShellReloading and ShellReloaded notification contracts in src/CShells/Notifications/ShellReloading.cs and src/CShells/Notifications/ShellReloaded.cs"
Task: "Emit aggregate and per-shell full-reload notifications in src/CShells/Management/DefaultShellManager.cs and src/CShells/Notifications/ShellsReloaded.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Foundational
2. Complete Phase 2: User Story 1
3. Validate strict targeted reload independently
4. Stop for review before broadening to full reload reconciliation and notifications

### Incremental Delivery

1. Finish Foundational to establish provider lookup and host invalidation seams
2. Deliver User Story 1 for strict targeted reload
3. Deliver User Story 2 for full reload reconciliation and stale-context correction
4. Deliver User Story 3 for explicit reload observability
5. Finish with docs and full regression validation

### Parallel Team Strategy

1. One developer updates provider implementations while another prepares provider lookup tests during Foundational work
2. After Foundational completes:
   - Developer A: US1 strict targeted reload
   - Developer B: US2 full reload reconciliation
   - Developer C: US3 reload notifications
3. Rejoin for polish and full-suite validation

---

## Notes

- [P] tasks touch different files with no dependency on incomplete work
- Story labels map directly to the prioritized user stories in spec.md
- Focused test files should stay aligned with the touched subsystem paths under `tests/CShells.Tests/`
- Keep changes minimal and preserve existing lifecycle behavior unless the spec requires an explicit reload-specific extension