---
description: "Task list for Shell Draining & Disposal Lifecycle"
---

# Tasks: Shell Draining & Disposal Lifecycle

**Input**: Design documents from `/specs/006-shell-drain-lifecycle/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The spec requires independently-testable user stories (see each story's "Independent Test") and the plan.md explicitly enumerates unit + integration test files under `tests/CShells.Tests/Unit/Lifecycle` and `tests/CShells.Tests/Integration/Lifecycle`.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story this task belongs to (US1…US5)
- File paths are absolute-from-repo-root

## Path Conventions

- **Abstractions**: `src/CShells.Abstractions/Lifecycle/`
- **Implementation**: `src/CShells/Lifecycle/` and `src/CShells/Lifecycle/Policies/`
- **Tests**: `tests/CShells.Tests/Unit/Lifecycle/` and `tests/CShells.Tests/Integration/Lifecycle/`
- Multi-target: library projects target `net8.0;net9.0;net10.0`; tests target `net10.0`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the directory scaffolding the feature expects.

- [ ] T001 Create directory `src/CShells.Abstractions/Lifecycle/` for new abstraction contracts
- [ ] T002 [P] Create directory `src/CShells/Lifecycle/` and `src/CShells/Lifecycle/Policies/` for implementations
- [ ] T003 [P] Create directory `tests/CShells.Tests/Unit/Lifecycle/` and `tests/CShells.Tests/Integration/Lifecycle/` for tests

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core types and interfaces that every user story depends on. These must compile cleanly before any user story implementation begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 [P] Extend `ShellId` to carry `Version` (breaking change per Principle VI) in `src/CShells.Abstractions/ShellId.cs` — add `Version` property, two-arg ctor, case-insensitive equality on `(Name, Version)`, guard both for null/whitespace; remove implicit `string → ShellId` conversion
- [ ] T005 [P] Create `ShellLifecycleState` enum (`Initializing`, `Active`, `Deactivating`, `Draining`, `Drained`, `Disposed`) in `src/CShells.Abstractions/Lifecycle/ShellLifecycleState.cs`
- [ ] T006 [P] Create `ShellDescriptor` immutable record (Name, Version, CreatedAt, Metadata) in `src/CShells.Abstractions/Lifecycle/ShellDescriptor.cs` with `ImmutableDictionary<string,string>.Empty` default metadata
- [ ] T007 [P] Create `IShell : IAsyncDisposable` interface (Descriptor, State, ServiceProvider) in `src/CShells.Abstractions/Lifecycle/IShell.cs`
- [ ] T008 [P] Create `IShellLifecycleSubscriber` interface (`OnStateChangedAsync`) in `src/CShells.Abstractions/Lifecycle/IShellLifecycleSubscriber.cs`
- [ ] T009 [P] Create `IDrainExtensionHandle` and `IDrainHandler` interfaces in `src/CShells.Abstractions/Lifecycle/IDrainExtensionHandle.cs` and `src/CShells.Abstractions/Lifecycle/IDrainHandler.cs`
- [ ] T010 [P] Create `IDrainPolicy` interface (`InitialTimeout`, `IsUnbounded`, `TryExtend`) in `src/CShells.Abstractions/Lifecycle/IDrainPolicy.cs`
- [ ] T011 [P] Create `DrainStatus` enum, `DrainResult` record, `DrainHandlerResult` record in `src/CShells.Abstractions/Lifecycle/DrainResult.cs` and `src/CShells.Abstractions/Lifecycle/DrainHandlerResult.cs`
- [ ] T012 [P] Create `IDrainOperation` interface (`Status`, `Deadline`, `WaitAsync`, `ForceAsync`) in `src/CShells.Abstractions/Lifecycle/IDrainOperation.cs`
- [ ] T013 Create `IShellRegistry` interface with full surface (`CreateAsync`, `GetActive`, `GetAll`, `PromoteAsync`, `DrainAsync`, `ReplaceAsync`, `Subscribe`, `Unsubscribe`) matching `contracts/IShellRegistry.md` in `src/CShells.Abstractions/Lifecycle/IShellRegistry.cs` — depends on T005–T012
- [ ] T014 Update existing callers of old single-argument `ShellId` constructor across `src/CShells.Abstractions/` and `src/CShells/` to supply a sentinel version string (e.g. `"__unversioned__"`) so the pre-existing `IShellManager`/`DefaultShellHost` code compiles per research Decision 1

**Checkpoint**: Abstractions compile; no implementation code yet. User stories can now proceed.

---

## Phase 3: User Story 1 - Observe shell state transitions (Priority: P1) 🎯 MVP

**Goal**: Shells transition monotonically through `Initializing → Active → Deactivating → Draining → Drained → Disposed` and fire state-change events to per-shell and global subscribers.

**Independent Test**: Create a shell, advance it through states programmatically, assert state-changed events fire in order with correct old/new values; assert attempts to move backward from `Disposed` are no-ops.

### Tests for User Story 1 ⚠️

> Write these tests FIRST and confirm they fail before implementing.

- [ ] T015 [P] [US1] Unit tests for CAS-based state machine (forward transitions, backward no-ops, event ordering) in `tests/CShells.Tests/Unit/Lifecycle/ShellStateMachineTests.cs`

### Implementation for User Story 1

- [ ] T016 [US1] Implement `Shell` class (`IShell`) with `Interlocked.CompareExchange`-based state field, `ShellDescriptor`, scoped `IServiceProvider`, `DisposeAsync` that transitions directly to `Disposed`, and per-shell state-change event fan-out in `src/CShells/Lifecycle/Shell.cs`
- [ ] T017 [US1] Implement `ShellRegistry` with `CreateAsync` (builds `ServiceCollection`, invokes `configure`, constructs `Shell`, rejects duplicate `ShellId` via `InvalidOperationException`, propagates configure exceptions without retaining partial entries), `GetActive`, `GetAll`, `Subscribe`, `Unsubscribe`, and `PromoteAsync` (per-name `SemaphoreSlim(1,1)`; atomic promote + deactivate-previous; subscriber exceptions caught + logged) in `src/CShells/Lifecycle/ShellRegistry.cs`
- [ ] T018 [P] [US1] Implement `ShellLifecycleLogger : IShellLifecycleSubscriber` that emits structured `ILogger` entries on every transition in `src/CShells/Lifecycle/ShellLifecycleLogger.cs`
- [ ] T019 [US1] Wire `AddCShells` in `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs` to register `IShellRegistry` singleton and auto-register + subscribe `ShellLifecycleLogger` (FR-021) so no host configuration is required

**Checkpoint**: User Story 1 is fully functional: hosts can create shells, promote them, observe transitions, and receive automatic log output.

---

## Phase 4: User Story 2 - Register drain handlers to complete in-flight work (Priority: P1)

**Goal**: Drain handlers registered in the shell's `IServiceCollection` are resolved and invoked in parallel when the shell enters `Draining`, each with a cancellation token tied to the drain deadline.

**Independent Test**: Register an `IDrainHandler` that records invocation and awaits a short delay; call `DrainAsync`; assert handler ran and received a cancellation token; assert shell transitioned to `Drained`.

### Tests for User Story 2 ⚠️

- [ ] T020 [P] [US2] Integration tests for drain handler invocation (single handler, multiple handlers in parallel, handler that throws, handler cancelled at deadline, zero-handlers fast-path per SC-007) in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryDrainTests.cs`

### Implementation for User Story 2

- [ ] T021 [P] [US2] Implement `FixedTimeoutDrainPolicy` (default) with `TryExtend` always returning `false` in `src/CShells/Lifecycle/Policies/FixedTimeoutDrainPolicy.cs`
- [ ] T022 [US2] Implement `DrainOperation : IDrainOperation` in `src/CShells/Lifecycle/DrainOperation.cs` — resolves `IEnumerable<IDrainHandler>` from the shell provider, invokes all in parallel via `Task.WhenAll`, creates per-handler `CancellationTokenSource` linked to deadline, captures exceptions into `DrainHandlerResult`, uses `TaskCompletionSource<DrainResult>` for `WaitAsync`
- [ ] T023 [US2] Add `DrainAsync` to `ShellRegistry` in `src/CShells/Lifecycle/ShellRegistry.cs` — idempotent via `Interlocked.CompareExchange` on per-shell `DrainOperation?`, transitions `Active`/`Deactivating` → `Draining`, throws `InvalidOperationException` for already-drained shells, auto-disposes shell provider after `Drained`
- [ ] T024 [US2] Register default `IDrainPolicy` (`FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30))`) via `TryAddSingleton` in `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs` so consumer overrides still win

**Checkpoint**: User Story 2 is fully functional: hosts can register drain handlers and see them run before disposal.

---

## Phase 5: User Story 3 - Replace an active shell while the old one drains (Priority: P2)

**Goal**: Promoting a new shell for a name while an active one exists immediately flips the active pointer and initiates a background drain on the old shell.

**Independent Test**: Create shell A `(payments, v1)`, promote; create shell B `(payments, v2)`; call `ReplaceAsync(B)`; assert `GetActive("payments") == B`, `A.State == Deactivating` (or already `Draining`), and services resolve from A's provider while it drains.

### Tests for User Story 3 ⚠️

- [ ] T025 [P] [US3] Integration tests for `ReplaceAsync` (old → Deactivating → Draining; new is Active; consumers holding A resolve services during drain) in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryReplaceTests.cs`
- [ ] T026 [P] [US3] Integration tests for concurrency (concurrent `PromoteAsync` same name — last-wins; concurrent `DrainAsync` returns same operation per FR-018/SC-004; concurrent `PromoteAsync` different names run in parallel) in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryConcurrencyTests.cs`

### Implementation for User Story 3

- [ ] T027 [US3] Implement `ReplaceAsync(newShell, ct)` on `ShellRegistry` in `src/CShells/Lifecycle/ShellRegistry.cs` — shares the per-name `SemaphoreSlim`, performs promote + drain atomically, returns `IDrainOperation?` (null if no prior active)

**Checkpoint**: User Story 3 is fully functional: rolling deployments become safe.

---

## Phase 6: User Story 4 - Await drain completion and inspect results (Priority: P2)

**Goal**: Callers can await a drain, inspect per-handler outcomes (completed / error / elapsed), and force-complete at any time.

**Independent Test**: Trigger drain with multiple handlers of varying behaviour (fast, throwing, slow); await `drainOp.WaitAsync`; assert one `DrainHandlerResult` per handler with correct flags. Separately: trigger drain, call `ForceAsync`, assert status is `Forced` and shell reached `Drained` within grace window.

### Tests for User Story 4 ⚠️

- [ ] T028 [P] [US4] Unit tests for `DrainOperation` — completed path populates per-handler `Elapsed` and `Error`; force path cancels handlers, reports `Forced` within grace period; timeout path reports `TimedOut` per SC-005 in `tests/CShells.Tests/Unit/Lifecycle/DrainOperationTests.cs`

### Implementation for User Story 4

- [ ] T029 [US4] Implement `ForceAsync` on `DrainOperation` in `src/CShells/Lifecycle/DrainOperation.cs` — cancels all handler CTS, waits up to grace period (default 3 s, configurable), forces `DrainStatus.Forced` and transitions shell to `Drained` even if handlers ignore cancellation
- [ ] T030 [US4] Populate `DrainResult` / `DrainHandlerResult` fully from `DrainOperation` (HandlerTypeName = `handler.GetType().Name`, Completed flag, Elapsed `Stopwatch`, Error) in `src/CShells/Lifecycle/DrainOperation.cs`
- [ ] T031 [US4] Expose configurable grace period (default `TimeSpan.FromSeconds(3)`) via a simple options record consumed by `DrainOperation`; register default via `TryAddSingleton` in `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs`

**Checkpoint**: User Story 4 is fully functional: operators get observability into drain outcomes; force works reliably.

---

## Phase 7: User Story 5 - Configure drain timeout policy (Priority: P3)

**Goal**: Hosts can pick between fixed, extensible, and unbounded drain timeout policies to match environment needs.

**Independent Test**: Configure `FixedTimeoutDrainPolicy(1s)`, register a handler that waits indefinitely, trigger drain, assert completion within ~1 s + grace with `TimedOut` status.

### Tests for User Story 5 ⚠️

- [ ] T032 [P] [US5] Unit tests for `FixedTimeoutDrainPolicy` (TryExtend always false; InitialTimeout surfaced) in `tests/CShells.Tests/Unit/Lifecycle/FixedTimeoutPolicyTests.cs`
- [ ] T033 [P] [US5] Unit tests for `ExtensibleTimeoutDrainPolicy` (grants up to cap; rejects once cap reached; cumulative extension tracking) in `tests/CShells.Tests/Unit/Lifecycle/ExtensibleTimeoutPolicyTests.cs`
- [ ] T034 [P] [US5] Unit tests for `UnboundedDrainPolicy` (InitialTimeout == null; IsUnbounded == true; TryExtend grants request; warning logged on first use) in `tests/CShells.Tests/Unit/Lifecycle/UnboundedPolicyTests.cs`

### Implementation for User Story 5

- [ ] T035 [P] [US5] Implement `ExtensibleTimeoutDrainPolicy(TimeSpan initial, TimeSpan cap)` with cumulative extension tracking in `src/CShells/Lifecycle/Policies/ExtensibleTimeoutDrainPolicy.cs`
- [ ] T036 [P] [US5] Implement `UnboundedDrainPolicy` (null `InitialTimeout`, `IsUnbounded = true`, `TryExtend` always true, `ILogger` warning emitted on first drain) in `src/CShells/Lifecycle/Policies/UnboundedDrainPolicy.cs`
- [ ] T037 [US5] Add `ConfigureDrainPolicy(IDrainPolicy)` builder method to `src/CShells/DependencyInjection/CShellsBuilderExtensions.cs` (or equivalent) so hosts can override the default policy per quickstart.md §1
- [ ] T038 [US5] Wire `DrainOperation` to resolve the configured `IDrainPolicy` from the root service provider (not the shell provider) so policy is a host-level concern per research Decision 8

**Checkpoint**: All user stories are independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, sample-app demonstration, and cleanup across stories.

- [ ] T039 Update the sample workbench to demonstrate the drain lifecycle end-to-end: (a) add a sample `IDrainHandler` (e.g. `WorkbenchDrainHandler` under `samples/CShells.Workbench.Features/Core/`) that awaits a configurable short delay to simulate in-flight work and logs start/finish/cancel; (b) register it in the relevant feature's `ConfigureServices` so it is picked up by the sample shells defined in `samples/CShells.Workbench/Shells/*.json`; (c) add a demo code path in `samples/CShells.Workbench/Background/ShellDemoWorker.cs` (or a new worker) that uses `IShellRegistry` to `CreateAsync` + `PromoteAsync` a v2 of an existing shell and calls `ReplaceAsync` to show rolling replacement while the old shell drains; (d) update `samples/CShells.Workbench/README.md` with a short "Drain lifecycle demo" section pointing at the new code. Verifies SC-001/SC-002/SC-006 behaviour in a runnable host.
- [ ] T040 [P] Verify all three library projects (`CShells.Abstractions`, `CShells`, consumers) build cleanly on `net8.0`, `net9.0`, `net10.0` via `dotnet build`
- [ ] T041 [P] Run the full test suite (`dotnet test`) and confirm all Unit and Integration lifecycle tests pass
- [ ] T042 [P] Run the sample workbench locally and exercise the drain demo from T039; confirm structured log output shows the full state-transition sequence and a populated `DrainResult`
- [ ] T043 Walk through `specs/006-shell-drain-lifecycle/quickstart.md` end-to-end against the implementation; fix any drift between the doc and the code
- [ ] T044 Review `Shell.cs`, `ShellRegistry.cs`, `DrainOperation.cs` for DRY opportunities, unnecessary comments, and any `lock()` around async paths (Principle VII) — clean up before handoff

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational
- **US2 (Phase 4)**: Depends on Foundational + US1 (needs `Shell`/`ShellRegistry` scaffolding)
- **US3 (Phase 5)**: Depends on US1 (promote) + US2 (drain)
- **US4 (Phase 6)**: Depends on US2 (DrainOperation exists)
- **US5 (Phase 7)**: Depends on US2 (policy abstraction in use); can run in parallel with US3/US4
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- US1 is the foundation: state machine + registry basics.
- US2 layers drain onto US1's shell/registry.
- US3, US4, US5 can be delivered in any order after US2:
  - US3 adds `ReplaceAsync`
  - US4 adds `ForceAsync` + structured results
  - US5 adds extensible/unbounded policies

### Within Each User Story

- Tests (T015, T020, T025/T026, T028, T032/T033/T034) are written first and must fail before implementation.
- Within US1: `Shell` (T016) → `ShellRegistry` (T017) → DI wiring (T019); logger (T018) is independent.
- Within US2: Policy (T021) and `DrainOperation` (T022) can start in parallel; `DrainAsync` on registry (T023) depends on T022; DI default (T024) depends on T021.
- Within US4: T029–T031 all edit `DrainOperation.cs` and must run sequentially.

### Parallel Opportunities

- Phase 2 foundational types T004–T012 are all in separate files — all marked [P].
- US1 T018 (logger) is parallel with T016/T017.
- US2 T021 (policy) is parallel with T022 (operation).
- US3 T025 and T026 test different scenarios in different files — parallel.
- US5 policies T035/T036 are parallel; tests T032–T034 are parallel.

---

## Parallel Example: Foundational Phase

```bash
# Launch all foundational abstraction tasks together:
Task: "Extend ShellId with Version in src/CShells.Abstractions/ShellId.cs"           # T004
Task: "Create ShellLifecycleState enum in .../Lifecycle/ShellLifecycleState.cs"       # T005
Task: "Create ShellDescriptor record in .../Lifecycle/ShellDescriptor.cs"             # T006
Task: "Create IShell interface in .../Lifecycle/IShell.cs"                            # T007
Task: "Create IShellLifecycleSubscriber in .../Lifecycle/IShellLifecycleSubscriber.cs" # T008
Task: "Create IDrainHandler + IDrainExtensionHandle"                                  # T009
Task: "Create IDrainPolicy in .../Lifecycle/IDrainPolicy.cs"                          # T010
Task: "Create DrainStatus/DrainResult/DrainHandlerResult"                             # T011
Task: "Create IDrainOperation in .../Lifecycle/IDrainOperation.cs"                    # T012
```

## Parallel Example: User Story 5

```bash
# Policy implementations and their tests in parallel:
Task: "Implement ExtensibleTimeoutDrainPolicy"                                         # T035
Task: "Implement UnboundedDrainPolicy"                                                 # T036
Task: "Unit tests for FixedTimeoutDrainPolicy"                                         # T032
Task: "Unit tests for ExtensibleTimeoutDrainPolicy"                                    # T033
Task: "Unit tests for UnboundedDrainPolicy"                                            # T034
```

---

## Implementation Strategy

### MVP (US1 + US2 = both P1)

1. Phase 1 Setup
2. Phase 2 Foundational (CRITICAL — blocks all stories)
3. Phase 3 US1 — state machine + events
4. Phase 4 US2 — drain handlers
5. **STOP and VALIDATE**: Both P1 stories pass their independent tests
6. Deploy/demo

At the MVP checkpoint, hosts can create shells, register drain handlers, drain cooperatively, and observe state transitions via the auto-registered logger.

### Incremental Delivery

1. MVP as above
2. Add US3 (`ReplaceAsync`) → demo rolling update
3. Add US4 (`ForceAsync` + structured results) → demo operator observability
4. Add US5 (extensible/unbounded policies) → demo configuration options
5. Phase 8 Polish → ship

### Parallel Team Strategy

After Foundational:
- Developer A: US1 → US3 (registry + replace)
- Developer B: US2 → US4 (drain + operation details)
- Developer C: US5 (policies, independent once US2's `IDrainPolicy` is resolvable)

---

## Notes

- [P] tasks touch different files with no cross-dependencies.
- Every drain handler exception MUST surface as `DrainHandlerResult.Error`; never swallowed (Principle IV).
- No `lock()` around async paths — use `SemaphoreSlim` or `Interlocked.*` (Principle VII).
- Subscriber exceptions are caught, logged, and swallowed; they MUST NOT abort transitions or other subscribers.
- Multi-target build must stay green on `net8.0;net9.0;net10.0` for every library project change.
- Keep constructor/field/primary-constructor style modern per Principle III.
