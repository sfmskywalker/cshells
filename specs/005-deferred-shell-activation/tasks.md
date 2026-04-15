# Tasks: Deferred Shell Activation and Atomic Shell Reconciliation

**Input**: Design documents from `/specs/005-deferred-shell-activation/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `quickstart.md`, `contracts/runtime-feature-catalog-contract.md`, `contracts/shell-runtime-reconciliation-contract.md`

**Tests**: Include focused xUnit and ASP.NET Core end-to-end coverage because the specification, plan, research, and quickstart explicitly require verification for desired-vs-applied shell state, refreshable feature catalog behavior, atomic reconciliation, active-only routing/endpoints, and strict explicit `Default` semantics.

**Organization**: Tasks are grouped by user story so the dual-state runtime model lands first, catalog refresh and atomic reconciliation build on that foundation second, and routing/operator visibility align to committed applied runtimes last.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., `US1`, `US2`, `US3`)
- Include exact file paths in descriptions

## Path Conventions

- Repository root: `/Users/sipke/Projects/ValenceWorks/cshells/main`
- Public contracts belong in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/`
- Runtime orchestration belongs in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/`
- Web/runtime exposure belongs in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/`
- Automated coverage belongs in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/` and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests.EndToEnd/`
- Operator/sample guidance updates stay limited to the lifecycle, resolution, runtime-management, and workbench assets already in scope for this feature

[US1]: #phase-3-user-story-1---preserve-live-service-while-new-desired-state-waits-priority-p1--mvp
[US2]: #phase-4-user-story-2---atomically-reconcile-all-shells-after-catalog-refresh-priority-p2
[US3]: #phase-5-user-story-3---expose-clear-desired-vs-applied-status-to-operators-and-routing-priority-p3

## Phase 1: Setup (Architecture Baseline)

**Purpose**: Lock the exact runtime, web, and test seams that the deferred-activation redesign will change before introducing new abstractions or reconciliation flow.

- [ ] T001 [P] Audit the current single-truth runtime seams in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ShellSettingsCache.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/DefaultShellHost.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/ShellStartupHostedService.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/DefaultShellManager.cs`
- [ ] T002 [P] Audit the current routing/endpoint/test touchpoints in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Resolution/DefaultShellResolverStrategy.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Notifications/ShellEndpointRegistrationHandler.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Routing/DynamicShellEndpointDataSource.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/AspNetCore/WebRoutingShellResolverTests.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Introduce the shared desired-vs-applied state contracts, internal state store, and refreshable catalog scaffolding that every user story depends on.

**⚠️ CRITICAL**: No user story work should begin until this phase is complete.

- [ ] T003 Create the public runtime-state inspection contracts in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Management/IShellRuntimeStateAccessor.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Management/ShellRuntimeStatus.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.Abstractions/Management/ShellReconciliationOutcome.cs`
- [ ] T004 [P] Add the internal desired/applied runtime records and state store in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/ShellRuntimeRecord.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/ShellRuntimeStateStore.cs`
- [ ] T005 [P] Add the refreshable runtime feature catalog snapshot/validation seam in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/RuntimeFeatureCatalog.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureDiscovery.cs`
- [ ] T006 Wire the shared runtime-state and catalog services through `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/ServiceCollectionExtensions.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/DefaultShellHost.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/DefaultShellManager.cs`

**Checkpoint**: The desired/applied read model, internal state storage, and refreshable catalog seam exist so user stories can build on one reconciliation architecture.

---

## Phase 3: User Story 1 - Preserve Live Service While New Desired State Waits (Priority: P1) 🎯 MVP

**Goal**: Record every shell’s latest desired generation immediately while preserving the last-known-good applied runtime through deferred and failed successor attempts, including strict explicit `Default` semantics.

**Independent Test**: Start with an active shell, update it to a desired generation that references an unavailable feature or fails candidate build, and confirm the new desired generation is recorded with a deferred/failed reason while the previous applied runtime remains active; if the shell is the explicit `Default`, fallback must report it unavailable instead of silently substituting another shell.

### Tests for User Story 1 ⚠️

> **NOTE**: Add these tests before finalizing implementation so the desired-vs-applied split, last-known-good preservation, and explicit `Default` behavior are locked by failing coverage first.

- [ ] T007 [P] [US1] Add desired-versus-applied status projection coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Management/ShellRuntimeStateAccessorTests.cs`
- [ ] T008 [P] [US1] Extend deferred/failed last-known-good preservation coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs`
- [ ] T009 [P] [US1] Add explicit configured `Default` unavailable fallback coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/ShellHost/DefaultShellFallbackIntegrationTests.cs`

### Implementation for User Story 1

- [ ] T010 [US1] Separate desired-state recording from applied-runtime commit in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Configuration/ShellSettingsCache.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/DefaultShellManager.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/ShellStartupHostedService.cs`
- [ ] T011 [US1] Implement candidate-build outcomes, blocking-reason capture, and last-known-good preservation in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/DefaultShellHost.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/ShellRuntimeStateStore.cs`
- [ ] T012 [US1] Enforce explicit `Default` applied-runtime semantics in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/IShellHost.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/DefaultShellHost.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Resolution/DefaultShellResolverStrategy.cs`

**Checkpoint**: Desired shell definitions remain authoritative, last-known-good runtimes stay active through deferred/failed successors, and explicit `Default` never silently falls through to another shell.

---

## Phase 4: User Story 2 - Atomically Reconcile All Shells After Catalog Refresh (Priority: P2)

**Goal**: Refresh the runtime feature catalog before every reconciliation pass, build candidate runtimes against the committed snapshot, and atomically promote only fully ready successors while preserving the previous catalog and applied runtimes on refresh failure.

**Independent Test**: Start with deferred and out-of-sync shells, make missing features discoverable, trigger single-shell and full reloads, and confirm the catalog refresh happens first, duplicate IDs abort the refresh without state mutation, and newly satisfiable shells atomically swap from their old applied runtime to the new committed runtime.

### Tests for User Story 2 ⚠️

- [ ] T013 [P] [US2] Add repeated provider-refresh and duplicate-feature-ID rollback coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/Features/RuntimeFeatureCatalogTests.cs`
- [ ] T014 [P] [US2] Extend mixed-shell reconciliation and atomic replacement coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/DefaultShellHost/LifecycleTests.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs`

### Implementation for User Story 2

- [ ] T015 [US2] Implement candidate catalog refresh, provider re-evaluation, and duplicate-ID abort behavior in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/RuntimeFeatureCatalog.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/FeatureAssemblyResolver.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Features/HostFeatureAssemblyProvider.cs`
- [ ] T016 [US2] Move `ReloadShellAsync(...)` and `ReloadAllShellsAsync()` onto refresh-first shell-agnostic reconciliation in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/DefaultShellManager.cs`
- [ ] T017 [US2] Implement atomic per-shell candidate commit and applied-runtime lifecycle notifications in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/DefaultShellHost.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Notifications/ShellActivated.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Notifications/ShellDeactivating.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Notifications/ShellReloading.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Notifications/ShellReloaded.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Notifications/ShellsReloaded.cs`

**Checkpoint**: Runtime feature discovery refreshes safely per reconciliation pass, duplicate IDs fail fast without mutating applied state, and newly satisfiable shells advance only through candidate-build then atomic commit.

---

## Phase 5: User Story 3 - Expose Clear Desired-vs-Applied Status to Operators and Routing (Priority: P3)

**Goal**: Make routing, endpoints, and operator-visible status reflect applied active runtimes only while still surfacing desired-state drift, blocking reasons, and unapplied shells clearly.

**Independent Test**: Reconcile shells into mixed states and confirm only committed applied runtimes resolve requests or expose endpoints, while runtime status inspection still reports desired generation, applied generation, outcome, sync/drift, and blocking details for every configured shell.

### Tests for User Story 3 ⚠️

- [ ] T018 [P] [US3] Add applied-active routing and unapplied-shell resolution coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/AspNetCore/WebRoutingShellResolverTests.cs`
- [ ] T019 [P] [US3] Add committed-runtime-only endpoint exposure coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/AspNetCore/ApplicationBuilderExtensionsTests.cs`
- [ ] T020 [P] [US3] Add mixed-state runtime status and strict explicit `Default` end-to-end coverage in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/ShellHost/ShellRuntimeStatusIntegrationTests.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests.EndToEnd/WebRoutingShellResolutionTests.cs`

### Implementation for User Story 3

- [ ] T021 [US3] Restrict shell retrieval, `AllShells`, and default-shell enumeration to committed applied runtimes in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/IShellHost.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/DefaultShellHost.cs`
- [ ] T022 [US3] Align request resolution to applied-active shells only in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Resolution/DefaultShellResolverStrategy.cs`
- [ ] T023 [US3] Align endpoint registration and dynamic routing refresh to applied-runtime commit events in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Notifications/ShellEndpointRegistrationHandler.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Routing/DynamicShellEndpointDataSource.cs`
- [ ] T024 [US3] Implement the operator-facing runtime status accessor in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/ShellRuntimeStateAccessor.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Management/DefaultShellManager.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/Hosting/DefaultShellHost.cs`

**Checkpoint**: Only committed applied runtimes are routable or endpoint-visible, and operators can inspect desired-versus-applied drift for every configured shell.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Update the in-scope guidance assets and validate the final architecture against the quickstart scenarios and targeted test suites.

- [ ] T025 [P] Update desired-vs-applied lifecycle and runtime-management guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/shell-lifecycle.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Runtime-Shell-Management.md`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Shell-Lifecycle.md`
- [ ] T026 [P] Update active-only routing and explicit `Default` guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/shell-resolution.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Shell-Resolution.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/README.md`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/Program.cs`
- [ ] T027 Run the quickstart validation flow from `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/005-deferred-shell-activation/quickstart.md` against `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/` and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests.EndToEnd/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1: Setup**: No dependencies; start immediately.
- **Phase 2: Foundational**: Depends on Phase 1 and blocks all user stories.
- **Phase 3: User Story 1 (P1)**: Depends on Phase 2 because desired/applied separation, last-known-good preservation, and explicit `Default` semantics are the architectural MVP.
- **Phase 4: User Story 2 (P2)**: Depends on User Story 1 because catalog refresh and atomic commit build directly on the dual-state runtime model and last-known-good behavior.
- **Phase 5: User Story 3 (P3)**: Depends on User Story 2 so routing, endpoints, and operator visibility reflect the final applied-runtime-only reconciliation semantics.
- **Phase 6: Polish**: Depends on the stories you intend to ship and should run after code, tests, and behavior are stable.

### User Story Dependencies

- **User Story 1 (P1)**: MVP; no dependency on later stories.
- **User Story 2 (P2)**: Depends on User Story 1 because refreshable catalogs and atomic candidate promotion need the desired/applied runtime split to exist first.
- **User Story 3 (P3)**: Depends on User Story 2 because active-only routing/endpoints and operator visibility must reflect the committed reconciliation semantics, not an intermediate model.

### Within Each User Story

- Add story-specific failing tests before finalizing implementation.
- Record or refresh desired/catalog state before mutating applied runtime state.
- Build candidate runtimes before publishing applied-runtime lifecycle events.
- Align routing/endpoints only after the committed applied-runtime semantics are stable.
- Finish each story with its independent verification scenario before moving to the next priority.

### Parallel Opportunities

- T001 and T002 can run in parallel during the architecture baseline audit.
- T004 and T005 can run in parallel after T003 defines the public read model.
- T007, T008, and T009 can run in parallel for User Story 1 because they touch different test files.
- T013 and T014 can run in parallel for User Story 2.
- T018, T019, and T020 can run in parallel for User Story 3.
- T025 and T026 can run in parallel during the final guidance update.

---

## Parallel Example: User Story 1

```bash
# Build the User Story 1 guardrails in parallel:
Task: "Add desired-versus-applied status projection coverage in tests/CShells.Tests/Unit/Management/ShellRuntimeStateAccessorTests.cs"
Task: "Extend deferred/failed last-known-good preservation coverage in tests/CShells.Tests/Unit/Management/DefaultShellManagerReloadTests.cs"
Task: "Add explicit configured Default unavailable fallback coverage in tests/CShells.Tests/Integration/ShellHost/DefaultShellFallbackIntegrationTests.cs"
```

## Parallel Example: User Story 2

```bash
# Split catalog-refresh and reconciliation verification across contributors:
Task: "Add repeated provider-refresh and duplicate-feature-ID rollback coverage in tests/CShells.Tests/Unit/Features/RuntimeFeatureCatalogTests.cs"
Task: "Extend mixed-shell reconciliation and atomic replacement coverage in tests/CShells.Tests/Integration/DefaultShellHost/LifecycleTests.cs and tests/CShells.Tests/Integration/DefaultShellHost/ReloadBehaviorTests.cs"
```

## Parallel Example: User Story 3

```bash
# Build routing, endpoint, and operator-visibility verification in parallel:
Task: "Add applied-active routing and unapplied-shell resolution coverage in tests/CShells.Tests/Integration/AspNetCore/WebRoutingShellResolverTests.cs"
Task: "Add committed-runtime-only endpoint exposure coverage in tests/CShells.Tests/Integration/AspNetCore/ApplicationBuilderExtensionsTests.cs"
Task: "Add mixed-state runtime status and strict explicit Default end-to-end coverage in tests/CShells.Tests/Integration/ShellHost/ShellRuntimeStatusIntegrationTests.cs and tests/CShells.Tests.EndToEnd/WebRoutingShellResolutionTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate that desired state and applied runtime state are separated, last-known-good runtimes stay live, and explicit `Default` remains strict before moving on.

### Incremental Delivery

1. Establish the public status contract, internal runtime state store, and refreshable catalog seam.
2. Deliver User Story 1 so every shell records desired intent separately from applied runtime state and preserves last-known-good service.
3. Deliver User Story 2 so reload and refresh flows re-evaluate provider assemblies, build candidates, and commit atomically.
4. Deliver User Story 3 so routing, endpoints, and operator visibility reflect committed applied runtimes only.
5. Finish with docs/sample updates and quickstart-driven validation.

### Parallel Team Strategy

1. Split the Phase 1 source audit from the web/test audit.
2. After T003 lands, have one contributor build the internal runtime state store while another builds the refreshable catalog seam.
3. During User Story 1, split unit coverage, manager reload coverage, and explicit `Default` integration coverage across contributors.
4. During User Story 2, split catalog-refresh validation from lifecycle/reload integration coverage.
5. During User Story 3, split routing, endpoint, and runtime-status coverage plus their corresponding implementation work by subsystem.

---

## Notes

- `[P]` tasks touch different files and can be completed in parallel safely once their prerequisites are satisfied.
- `[US1]`, `[US2]`, and `[US3]` map directly to the prioritized stories in `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/005-deferred-shell-activation/spec.md`.
- The task order intentionally mirrors the chosen architecture: separate desired state from applied runtime state first, refresh and validate the runtime feature catalog second, then build candidate runtimes and commit atomically before exposing applied state to routing and operator tooling.
- Keep the implementation focused on the listed contracts, runtime orchestration seams, web integration points, and in-scope guidance assets; avoid reintroducing silent feature removal, configured-shell routing, or non-atomic runtime replacement paths.

