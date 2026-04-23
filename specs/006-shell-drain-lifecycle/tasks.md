---
description: "Task list for Shell Generations, Reload & Disposal Lifecycle — clean overhaul"
---

# Tasks: Shell Generations, Reload & Disposal Lifecycle

**Input**: Design documents from `/specs/006-shell-drain-lifecycle/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Scope**: This is a **clean overhaul**. The legacy `IShellHost` / `IShellManager` /
`ShellContext` / `IShellSettingsProvider` / `IShellActivatedHandler` /
`IShellDeactivatingHandler` surface is deleted; every downstream project is migrated in-place
(FR-038, FR-039). No legacy surface remains after this feature ships.

**Tests**: Included. The spec requires independently-testable user stories (see each story's
"Independent Test") and plan.md enumerates unit + integration test files under
`tests/CShells.Tests/Unit/Lifecycle` and `tests/CShells.Tests/Integration/Lifecycle`.

**Organization**: Phases 1–10 build the new surface. Phases 11–14 migrate downstream
consumers off the legacy surface. Phase 15 deletes the legacy types. Phase 16 is polish.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story this task belongs to (US1…US9). Tasks not tied to a user story
  have no `[Story]` marker.
- File paths are absolute-from-repo-root.

## Path Conventions

- **Abstractions**: `src/CShells.Abstractions/Lifecycle/`
- **Implementation**: `src/CShells/Lifecycle/`, `src/CShells/Lifecycle/Blueprints/`, `src/CShells/Lifecycle/Policies/`
- **Tests**: `tests/CShells.Tests/Unit/Lifecycle/` and `tests/CShells.Tests/Integration/Lifecycle/`
- Multi-target: library projects target `net8.0;net9.0;net10.0`; tests target `net10.0`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create directory scaffolding for the new lifecycle surface.

- [X] T001 Create directory `src/CShells.Abstractions/Lifecycle/`
- [X] T002 [P] Create directories `src/CShells/Lifecycle/`, `src/CShells/Lifecycle/Blueprints/`, and `src/CShells/Lifecycle/Policies/`
- [X] T003 [P] Create directories `tests/CShells.Tests/Unit/Lifecycle/`, `tests/CShells.Tests/Unit/Lifecycle/Blueprints/`, and `tests/CShells.Tests/Integration/Lifecycle/`

---

## Phase 2: Foundational Abstractions (Blocking Prerequisites)

**Purpose**: Create every public abstraction type. These must compile cleanly before any
implementation work begins.

**⚠️ CRITICAL**: `ShellId` stays unchanged. No types are modified in this phase; only added.

- [X] T004 [P] Create `ShellLifecycleState` enum (`Initializing`, `Active`, `Deactivating`, `Draining`, `Drained`, `Disposed`) in `src/CShells.Abstractions/Lifecycle/ShellLifecycleState.cs` (FR-017)
- [X] T005 [P] Create `ShellDescriptor` immutable record (`Name`, `Generation`, `CreatedAt`, `Metadata`, `ToString → "Name#Generation"`) with `ImmutableDictionary<string,string>.Empty` default metadata in `src/CShells.Abstractions/Lifecycle/ShellDescriptor.cs` (FR-004, FR-033)
- [X] T006 [P] Create `IShellScope : IAsyncDisposable` interface (`Shell`, `ServiceProvider`) in `src/CShells.Abstractions/Lifecycle/IShellScope.cs` (FR-020)
- [X] T007 [P] Create `IShell` interface (`Descriptor`, `State`, `ServiceProvider`, `BeginScope() → IShellScope`) in `src/CShells.Abstractions/Lifecycle/IShell.cs` — depends on T005, T006. **`IShell` does NOT implement `IDisposable` / `IAsyncDisposable`**; shell disposal is registry-owned (FR-037)
- [X] T008 [P] Create `IShellBlueprint` interface (`Name`, `Metadata`, `ComposeAsync`) in `src/CShells.Abstractions/Lifecycle/IShellBlueprint.cs` (FR-001, research Decision 2)
- [X] T009 [P] Create `IShellInitializer` interface (`InitializeAsync`) in `src/CShells.Abstractions/Lifecycle/IShellInitializer.cs` (FR-015, research Decision 6)
- [X] T010 [P] Create `IShellLifecycleSubscriber` interface (`OnStateChangedAsync`) in `src/CShells.Abstractions/Lifecycle/IShellLifecycleSubscriber.cs` (FR-019)
- [X] T011 [P] Create `IDrainExtensionHandle` and `IDrainHandler` interfaces in `src/CShells.Abstractions/Lifecycle/IDrainExtensionHandle.cs` and `src/CShells.Abstractions/Lifecycle/IDrainHandler.cs` (FR-023, FR-025, FR-026). Bundled because the two interfaces reference each other (`IDrainHandler.DrainAsync` takes an `IDrainExtensionHandle`); authoring them in a single commit avoids a temporary forward reference
- [X] T012 [P] Create `IDrainPolicy` interface (`InitialTimeout`, `IsUnbounded`, `TryExtend`) in `src/CShells.Abstractions/Lifecycle/IDrainPolicy.cs` (FR-027)
- [X] T013 [P] Create `DrainStatus` enum, `DrainResult` record (including `ScopeWaitElapsed`), and `DrainHandlerResult` record in `src/CShells.Abstractions/Lifecycle/DrainResult.cs` and `src/CShells.Abstractions/Lifecycle/DrainHandlerResult.cs` (FR-029)
- [X] T014 [P] Create `IDrainOperation` interface (`Status`, `Deadline`, `WaitAsync`, `ForceAsync`) in `src/CShells.Abstractions/Lifecycle/IDrainOperation.cs` (FR-029, FR-030)
- [X] T015 [P] Create `ReloadResult` record (`Name`, `NewShell`, `Drain`, `Error`) in `src/CShells.Abstractions/Lifecycle/ReloadResult.cs` (FR-012)
- [X] T016 Create `IShellRegistry` interface with the full surface from `contracts/IShellRegistry.md` in `src/CShells.Abstractions/Lifecycle/IShellRegistry.cs` — depends on T004–T015
- [X] T016a [P] Add a regression test `ShellIdShapeTests.cs` under `tests/CShells.Tests/Unit/` asserting via reflection that `ShellId` is a `readonly record struct` with exactly one public instance property named `Name` of type `string` and no `Generation` / `Version` / equivalent field — guards FR-008 against silent reintroduction of a composite identity

**Checkpoint**: Abstractions compile; no implementation code yet; no legacy code touched.

---

## Phase 3: User Story 8 — Observe shell state transitions (Priority: P2) 🎯 Foundation

**Goal**: Shells transition monotonically through the lifecycle states and fan out
state-change events to global subscribers. The auto-registered logging subscriber emits
structured logs with no host configuration.

**Why this story first**: Every downstream story relies on the `Shell` class and the
registry's event plumbing. Landing US8 first yields a testable `Shell` + event bus before
any activation or drain logic.

**Independent Test**: Construct a `Shell` directly in tests, advance it through states
programmatically, assert backward attempts from `Disposed` are no-ops and that every forward
transition fires an event with correct old/new values and descriptor metadata.

### Tests for User Story 8 ⚠️

- [X] T017 [P] [US8] Unit tests for CAS-based state machine in `tests/CShells.Tests/Unit/Lifecycle/ShellStateMachineTests.cs` — forward transitions per data-model table; backward attempts are no-ops; event ordering preserved; subscriber exceptions caught + logged + swallowed per research Decision 12; emergency-dispose path from any non-terminal state transitions directly to `Disposed` (this is the only non-`Drained → Disposed` path and is used by the registry on shutdown-timeout breach per FR-036); `IShell` does NOT expose `DisposeAsync` on its public surface (assert via reflection / compile-time) (FR-017, FR-018, FR-019, FR-037)

### Implementation for User Story 8

- [X] T018 [US8] Implement `Shell` class in `src/CShells/Lifecycle/Shell.cs` with `Interlocked.CompareExchange`-based `int _state`, a `ShellDescriptor`, the owned `IServiceProvider`, and per-instance state-change hooks the registry invokes. Shell disposal is registry-owned: expose an `internal ValueTask DisposeAsync()` that the registry calls after drain completes (normal path) or on shutdown-timeout breach (emergency path, FR-036); do NOT implement `IAsyncDisposable` on the public `IShell` surface (FR-037). Leave scope counter + `BeginScope` as TODO stubs (US6 will fill them in)
- [X] T019 [US8] Create `ShellRegistry` skeleton in `src/CShells/Lifecycle/ShellRegistry.cs` with thread-safe `Subscribe` / `Unsubscribe`, a `FireStateChangedAsync` helper that awaits subscribers sequentially but catches + logs each subscriber's exception so one bad subscriber cannot block others (research Decision 12, Principle VII)
- [X] T020 [P] [US8] Implement `ShellLifecycleLogger : IShellLifecycleSubscriber` in `src/CShells/Lifecycle/ShellLifecycleLogger.cs` — emits one structured `ILogger` entry per transition with required properties `ShellName` (string), `Generation` (int), `PreviousState` (string, null for initial `Initializing`), `CurrentState` (string); drain-completion entries additionally include `ScopeWaitElapsedMs` (long) and `HandlerCount` (int). Routine transitions at `LogLevel.Information`; drain timeouts / forced drains / abandoned scopes at `LogLevel.Warning`. Event IDs are stable and live in the reserved range 1000–1099 (1000–1009 transitions, 1010–1019 drain warnings). Add `ShellLifecycleLoggerTests.cs` under `tests/CShells.Tests/Unit/Lifecycle/` using a `FakeLogger` (or equivalent) to assert property presence, log level, and event ID for each scenario (FR-034, FR-034a, SC-008)
- [X] T021 [US8] Rewrite `AddCShells` in `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs` to register `IShellRegistry` as a singleton and auto-register + subscribe `ShellLifecycleLogger`. Remove all legacy wiring (`IShellHost`, `IShellManager`, `IShellSettingsProvider`, `IShellContextScopeFactory`, caches, hosted services) from this file (FR-034, FR-038)

**Checkpoint**: A `Shell` can be advanced through states; every transition is observable via
the auto-registered logger; no activation or drain logic exists yet.

---

## Phase 4: User Story 1 + User Story 4 — Activate a shell from a blueprint, with initializers (Priority: P1)

**Goal**: Hosts register one blueprint per shell name (fluent or config-backed). `ActivateAsync(name)` composes fresh `ShellSettings`, builds the shell's provider, runs its `IShellInitializer` services sequentially, stamps generation 1, and promotes to `Active`.

**Bundling note**: US1 (P1) and US4 (P2) retain their distinct priorities and have separate "Independent Tests" per spec. They are bundled here because initializer invocation is part of the `ActivateAsync` implementation path, sequenced inside the `Initializing → Active` transition (FR-016). The two test sets (T024 for US1, T025 for US4) remain independently evaluable: T024 passes with zero-initializer blueprints, so US1 is demonstrably testable without US4. If work needs to be sequenced across developers, complete T026–T029 (US1 core) first, then T025 + initializer wiring inside T029's promote step.

**Independent Tests**:
- **US1**: Register a `DelegateShellBlueprint` for `payments`; call `ActivateAsync("payments")`; assert the returned shell has `Descriptor.Generation == 1`, `State == Active`, and `registry.GetActive("payments")` returns it.
- **US4**: Register a feature that contributes multiple `IShellInitializer` services; activate; assert every initializer ran exactly once, in DI-registration order, before the shell became `Active`.

### Tests ⚠️

- [X] T022 [P] [US1] Unit tests for `DelegateShellBlueprint` (ComposeAsync invokes the stored delegate against a fresh `ShellBuilder` each time; metadata flows onto descriptor) in `tests/CShells.Tests/Unit/Lifecycle/Blueprints/DelegateShellBlueprintTests.cs` (FR-001, FR-002)
- [X] T023 [P] [US1] Unit tests for `ConfigurationShellBlueprint` (ComposeAsync re-reads the bound section so edits between reloads are picked up) in `tests/CShells.Tests/Unit/Lifecycle/Blueprints/ConfigurationShellBlueprintTests.cs` (FR-002)
- [X] T024 [P] [US1] Integration tests for `ActivateAsync` in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryActivateTests.cs` — generation 1 stamped, transitions fire, duplicate blueprint throws per FR-003, activation without a blueprint throws, **activation when an `Active` generation already exists throws `InvalidOperationException` with a message pointing callers at `ReloadAsync` (FR-009)**, composition exception propagates and leaves no partial entry per FR-014, `ShellSettings.Id.Name` mismatch throws
- [X] T025 [P] [US4] Integration tests for initializer invocation in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryInitializerTests.cs` — sequential execution in DI-registration order, throwing initializer aborts activation and disposes partial provider (FR-014), shell with no initializers activates immediately (FR-015, FR-016)

### Implementation

- [X] T026 [P] [US1] Implement `DelegateShellBlueprint` in `src/CShells/Lifecycle/Blueprints/DelegateShellBlueprint.cs` — stores an `Action<ShellBuilder>` (or overloads), invokes it against a new `ShellBuilder(name)` in `ComposeAsync`, exposes optional static `Metadata`
- [X] T027 [P] [US1] Implement `ConfigurationShellBlueprint` in `src/CShells/Lifecycle/Blueprints/ConfigurationShellBlueprint.cs` — binds a named `IConfigurationSection` (or a `ShellConfig`) on each compose so config edits between reloads are picked up (research Decision 2)
- [X] T028 [US1] Add `RegisterBlueprint` / `GetBlueprint` / `GetBlueprintNames` to `ShellRegistry` in `src/CShells/Lifecycle/ShellRegistry.cs` — case-insensitive name keying, duplicate registration throws `InvalidOperationException` (FR-003)
- [X] T029 [US1, US4] Implement `ActivateAsync(name, ct)` on `ShellRegistry` — acquires the per-name `SemaphoreSlim(1,1)`, invokes `blueprint.ComposeAsync`, validates `settings.Id.Name` matches, builds a fresh `IServiceCollection` (copies root services, applies feature `ConfigureServices`, registers a singleton `ShellConfiguration` for the merged `IConfiguration` view so services resolved from the shell provider see the shell-scoped configuration — this replaces the `DefaultShellHost.BuildProvider` wiring at the legacy equivalent site), constructs the provider, stamps generation 1 with blueprint metadata, registers as `Initializing`, resolves `IEnumerable<IShellInitializer>` and awaits each sequentially, promotes to `Active`, fires transitions, releases the semaphore; propagates any composition / build / initializer exception without retaining a partial entry (disposing the partial provider if built); throws `InvalidOperationException` if a generation is already `Active` for `name` (FR-009, FR-014, FR-015, FR-016, research Decisions 3, 6)
- [X] T030 [US1] Implement `GetActive(name)` and `GetAll(name)` on `ShellRegistry` (FR-031, FR-032)
- [X] T031 [US1] Add `AddShell(name, Action<ShellBuilder>)` and an overload accepting blueprint metadata to `src/CShells/DependencyInjection/CShellsBuilderExtensions.cs` so hosts register blueprints fluently during `AddCShells(...)` (quickstart §1)

**Checkpoint**: Hosts can register blueprints and activate shells; initializers run in order;
generation 1 is stamped automatically; the auto-logger shows the full activation transition
sequence.

---

## Phase 5: User Story 6 — In-flight scopes complete before disposal (Priority: P1)

**Goal**: `IShell.BeginScope()` returns an `IShellScope` that creates a DI scope and
increments a thread-safe active-scope counter on the shell. The counter is visible to the
drain machinery so drain's phase 1 can wait for it to reach zero before invoking handlers.

**Independent Test**: Acquire multiple `IShellScope` handles from a shell; assert the
counter reflects outstanding handles; dispose them; assert the counter returns to zero;
assert the handles' `ServiceProvider` behaves as an independent DI scope.

### Tests ⚠️

- [X] T032 [P] [US6] Unit tests for `Shell.BeginScope` and `ShellScope` in `tests/CShells.Tests/Unit/Lifecycle/ShellScopeTests.cs` — counter increments/decrements correctly under concurrent callers, disposing a scope disposes its DI scope, `BeginScope` on `Disposed` shell throws `InvalidOperationException`, `BeginScope` during `Draining` succeeds and joins the counter (FR-020, FR-021)

### Implementation

- [X] T033 [US6] Implement `ShellScope : IShellScope` in `src/CShells/Lifecycle/ShellScope.cs` — wraps an `AsyncServiceScope`, exposes `Shell` and `ServiceProvider`, calls back into `Shell` on dispose to decrement the counter
- [X] T034 [US6] Flesh out the scope counter + `BeginScope` implementation on `Shell` in `src/CShells/Lifecycle/Shell.cs` — `Interlocked.Increment/Decrement` on `_activeScopes`, a `TaskCompletionSource` (or manual-reset event) signalling counter-drops for drain phase 1 to await, throws if called after `Disposed` (FR-020, FR-021, research Decision 11)

**Checkpoint**: Shells expose tracked scopes. Drain phase 1 will consume the counter
mechanism in the next phase.

---

## Phase 6: User Story 5 — Drain handlers run after scope-wait (Priority: P1)

**Goal**: `DrainAsync` runs phase 1 (scope wait, bounded by deadline) then phase 2 (parallel
handler invocation). Drain handlers receive a cancellation token cancelled at the deadline;
handler exceptions are captured and do not abort peers.

**Independent Tests**:
- **Core drain**: Register a feature that contributes an `IDrainHandler` recording
  invocation and awaiting a short delay. Activate; call `registry.DrainAsync(shell)`;
  assert the handler ran with a cancellation token and the shell transitioned through
  `Draining → Drained → Disposed`.
- **Scope wait**: Acquire one or more `IShellScope` handles; call `DrainAsync`; assert
  handler invocation is deferred until the handles are disposed (or the deadline elapses).

### Tests ⚠️

- [X] T035 [P] [US5] Integration tests for drain-handler invocation in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryDrainTests.cs` — single handler; multiple handlers in parallel; a `[Theory]` with `[InlineData(0)]`, `[InlineData(1)]`, `[InlineData(50)]` covering the full 0-to-50-handlers range asserted by SC-004 (zero-handler path also covers SC-009); throwing handler captured in `DrainHandlerResult.Error` without aborting peers (FR-024); concurrent `DrainAsync` returns the same operation (FR-028, SC-006)
- [X] T036 [P] [US5] Integration tests for drain scope-wait phase in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryScopeWaitTests.cs` — outstanding scopes delay handler invocation (FR-022), scope release during phase 1 proceeds to phase 2 immediately, scopes outstanding at the deadline are abandoned and phase 2 runs with cancelled token (SC-010), `DrainResult.ScopeWaitElapsed` is populated, `DrainResult.AbandonedScopeCount` is zero in the normal case and equals the outstanding-handle count when the deadline bounds out phase 1

### Implementation

- [X] T037 [P] [US5] Implement `FixedTimeoutDrainPolicy` (default 30 s; `TryExtend` always `false`) in `src/CShells/Lifecycle/Policies/FixedTimeoutDrainPolicy.cs` (FR-027)
- [X] T038 [US5] Implement `DrainOperation : IDrainOperation` in `src/CShells/Lifecycle/DrainOperation.cs` — phase 1 awaits `Shell`'s active-scope counter reaching zero bounded by the deadline (captures elapsed time into `DrainResult.ScopeWaitElapsed`); phase 2 resolves `IEnumerable<IDrainHandler>`, creates per-handler linked `CancellationTokenSource`s tied to the remaining deadline, invokes all handlers in parallel via `Task.WhenAll`, captures exceptions into `DrainHandlerResult`; uses a `TaskCompletionSource<DrainResult>` for `WaitAsync`; implements `IDrainExtensionHandle` by delegating to the resolved `IDrainPolicy` (FR-022, FR-024–FR-026, FR-029, research Decisions 7–9, 11)
- [X] T039 [US5] Implement `DrainAsync(shell, ct)` on `ShellRegistry` in `src/CShells/Lifecycle/ShellRegistry.cs` — idempotent via `Interlocked.CompareExchange` on per-shell `DrainOperation?` slot (FR-028), transitions `Active`/`Deactivating → Draining`, awaits phase completion, transitions to `Drained`, disposes the shell provider, fires `Drained → Disposed`
- [X] T040 [US5] Register default `IDrainPolicy` (`FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30))`) via `TryAddSingleton` in `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs` so host overrides still win

**Checkpoint**: Full drain path works end-to-end; scope-tracked request work completes
before handlers run; shells complete the full `Active → Disposed` sequence after
`DrainAsync`.

---

## Phase 7: User Story 7 — Await drain completion, force, and inspect results (Priority: P2)

**Goal**: Callers can await a drain, inspect per-handler outcomes, and force-complete at
any time within the configured grace period.

**Independent Test**: Trigger drain with multiple handlers of varying behaviour (fast,
throwing, slow); await `drainOp.WaitAsync`; assert one `DrainHandlerResult` per handler
with correct flags. Separately: trigger drain, call `ForceAsync`, assert status is `Forced`
and the shell reached `Drained` within the grace window (SC-007).

### Tests ⚠️

- [X] T041 [P] [US7] Unit tests for `DrainOperation` in `tests/CShells.Tests/Unit/Lifecycle/DrainOperationTests.cs` — completed path populates per-handler `Elapsed`/`Completed`/`Error`; force path cancels handlers and reports `Forced` within grace period; timeout path reports `TimedOut`; overall `DrainStatus` reflects the dominant outcome (FR-029, FR-030, SC-007); force during phase 1 skips remaining scope-wait

### Implementation

- [X] T042 [US7] Implement `ForceAsync` on `DrainOperation` in `src/CShells/Lifecycle/DrainOperation.cs` — cancels the scope-wait and every handler CTS, waits up to the grace period (default 3 s, configurable), forces `DrainStatus.Forced`, transitions the shell to `Drained` even if handlers ignore cancellation (FR-030, SC-007)
- [X] T043 [US7] Populate `DrainResult` / `DrainHandlerResult` fully in `DrainOperation` — `HandlerTypeName = handler.GetType().Name`, `Completed` flag, `Elapsed` from a per-handler `Stopwatch`, `Error` from captured exception; attach the shell's `ShellDescriptor` to `DrainResult.Shell`; snapshot `AbandonedScopeCount` from the scope counter at the moment phase 1 completes (FR-029, data-model)
- [X] T044 [US7] Expose a configurable grace period (default 3 s) via a simple options record resolved by `DrainOperation`; register the default via `TryAddSingleton` in `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs` and expose `ConfigureGracePeriod(TimeSpan)` on `CShellsBuilderExtensions` (SC-007, quickstart §1)

**Checkpoint**: Operators can force a drain, observe per-handler outcomes, and rely on
`Drained` within `T + G` seconds.

---

## Phase 8: User Story 2 — Reload a shell (Priority: P1)

**Goal**: `ReloadAsync(name)` composes fresh settings, builds generation N+1, runs
initializers, promotes to `Active`, and initiates a background drain on generation N.

**Independent Test**: Register a blueprint whose features can be toggled via a mutable
source; activate (gen 1); mutate the source to add a feature; call `ReloadAsync("payments")`;
assert `GetActive("payments").Descriptor.Generation == 2`, the new generation resolves the
newly-added feature's services, and the previous generation is in `Draining` or `Drained`.

### Tests ⚠️

- [X] T045 [P] [US2] Integration tests in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryReloadTests.cs` — gen N+1 becomes `Active` while gen N moves through `Deactivating → Draining → Drained → Disposed`; services resolved from gen N continue to work during its drain; calling reload before any activation behaves like `ActivateAsync` (FR-011); reload with no blueprint throws; composition/initializer exceptions propagate without affecting the current active generation and without retaining a partial entry (FR-014); `ReloadResult.Drain` is null when no prior active generation existed; `ReloadResult.NewShell` carries the new generation
- [X] T046 [P] [US2] Integration tests for concurrency in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryConcurrencyTests.cs` — concurrent `ReloadAsync` for the same name serialize and assign strictly monotonic generations (FR-013, SC-005); concurrent `ReloadAsync` for different names run in parallel; generation number is "skipped" when composition/initializer fails (research Decision 3)

### Implementation

- [X] T047 [US2] Implement `ReloadAsync(name, ct)` on `ShellRegistry` in `src/CShells/Lifecycle/ShellRegistry.cs` — acquires the per-name semaphore, increments the generation counter, composes via the blueprint, builds the new generation in `Initializing`, runs initializers, promotes to `Active`, transitions the previous `Active` (if any) to `Deactivating` under the same semaphore, releases the semaphore, then kicks off `DrainAsync(previous)` outside the lock; returns a populated `ReloadResult` including the drain operation (FR-010, FR-013, FR-014, research Decision 3)
- [X] T048 [US2] Ensure `ReloadAsync` falls through to the single-generation path when no prior active exists so it behaves exactly like `ActivateAsync` with `ReloadResult.Drain == null` (FR-011)

**Checkpoint**: Full reload rollover works end-to-end; new generation serves new traffic
while the old generation drains cooperatively and in-flight scopes finish.

---

## Phase 9: User Story 3 — Reload every shell in one call (Priority: P2)

**Goal**: `ReloadAllAsync()` reloads every registered blueprint; per-name failures do not
abort the batch.

### Tests ⚠️

- [X] T049 [P] [US3] Integration tests in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryReloadAllTests.cs` — all registered names reload to a new generation; one failing blueprint surfaces its exception in the per-name `ReloadResult.Error` without aborting peers (FR-012, SC-003); a name never activated is activated as generation 1 with `Drain == null`; independent names reload in parallel

### Implementation

- [X] T050 [US3] Implement `ReloadAllAsync(ct)` on `ShellRegistry` in `src/CShells/Lifecycle/ShellRegistry.cs` — snapshots the current blueprint-name set, awaits a `ReloadAsync` per name via `Task.WhenAll`, captures exceptions into `ReloadResult.Error` so the batch never throws (FR-012)

**Checkpoint**: Operators can roll over every shell with one call and inspect per-name
outcomes.

---

## Phase 10: User Story 9 — Configure drain timeout policy (Priority: P3)

**Goal**: Hosts can pick between fixed, extensible, and unbounded drain timeout policies.

**Independent Test**: Configure `FixedTimeoutDrainPolicy(1s)`, register a handler that waits
indefinitely, trigger drain, assert completion within ~1 s + grace with `TimedOut` status.

### Tests ⚠️

- [X] T051 [P] [US9] Unit tests for `FixedTimeoutDrainPolicy` in `tests/CShells.Tests/Unit/Lifecycle/FixedTimeoutPolicyTests.cs`
- [X] T052 [P] [US9] Unit tests for `ExtensibleTimeoutDrainPolicy` (grants up to cap, rejects once cap reached, cumulative extension tracking) in `tests/CShells.Tests/Unit/Lifecycle/ExtensibleTimeoutPolicyTests.cs`
- [X] T053 [P] [US9] Unit tests for `UnboundedDrainPolicy` (`InitialTimeout == null`, `IsUnbounded == true`, warning logged on first use) in `tests/CShells.Tests/Unit/Lifecycle/UnboundedPolicyTests.cs`

### Implementation

- [X] T054 [P] [US9] Implement `ExtensibleTimeoutDrainPolicy(initial, cap)` with thread-safe cumulative extension tracking in `src/CShells/Lifecycle/Policies/ExtensibleTimeoutDrainPolicy.cs` (FR-026)
- [X] T055 [P] [US9] Implement `UnboundedDrainPolicy` (`null` `InitialTimeout`, `IsUnbounded = true`, `TryExtend` always true, `ILogger` warning on first drain) in `src/CShells/Lifecycle/Policies/UnboundedDrainPolicy.cs`
- [X] T056 [US9] Add `ConfigureDrainPolicy(IDrainPolicy)` builder method to `src/CShells/DependencyInjection/CShellsBuilderExtensions.cs` so hosts can override the default per quickstart §1 and §7
- [X] T057 [US9] Ensure `DrainOperation` resolves the configured `IDrainPolicy` from the root service provider (not the shell provider) so policy is a host-level concern (research Decision 10)

**Checkpoint**: All three policy types behave per spec; hosts can swap policies.

---

## Phase 11: Startup & Shutdown Integration

**Purpose**: Auto-activate every blueprint at host start; drain all active shells at host
stop. Failure of any startup activation fails host startup (FR-035). Shutdown drain is
bounded by the host's shutdown timeout; providers are disposed on timeout regardless (FR-036).

### Tests ⚠️

- [X] T058 [P] Integration tests for startup + shutdown in `tests/CShells.Tests/Integration/Lifecycle/ShellRegistryShutdownTests.cs` — every registered blueprint activates in parallel; a failing blueprint surfaces its exception and fails host start (FR-035); shutdown drains every active shell in parallel using the configured policy; shutdown completes even when a drain exceeds the host's shutdown timeout, disposing providers regardless (FR-036); the emergency-dispose path — and only this path — transitions a shell from a non-`Drained` state directly to `Disposed`, fires the corresponding state-change event, and disposes the provider; `IShell` exposes no public disposal method (FR-037)

### Implementation

- [X] T059 Implement `CShellsStartupHostedService` in `src/CShells/Hosting/CShellsStartupHostedService.cs` — `StartAsync` iterates `registry.GetBlueprintNames()` and awaits `registry.ActivateAsync(name)` via `Task.WhenAll`, surfacing any exception; `StopAsync` hooks `IHostApplicationLifetime.ApplicationStopping` and drains every currently-`Active` shell in parallel bounded by the shutdown token; disposes every shell (including any whose drain exceeded the timeout) before returning (FR-035, FR-036, research Decision 14)
- [X] T060 Register `CShellsStartupHostedService` via `AddHostedService<>` in `src/CShells/DependencyInjection/ServiceCollectionExtensions.cs`

**Checkpoint**: The library now owns shell lifecycle end-to-end from host start to host stop.

---

## Phase 12: Migrate `CShells.AspNetCore`

**Purpose**: Rewire the AspNetCore integration off the legacy `IShellHost` / `ShellContext` /
`IShellContextScope` surface onto `IShellRegistry` / `IShell` / `IShellScope` (FR-039).

- [ ] T061 Rewrite `src/CShells.AspNetCore/Middleware/ShellMiddleware.cs` to: resolve the current shell via `IShellResolver`, call `registry.GetActive(name)`, use `shell.BeginScope()` to get an `IShellScope`, set `HttpContext.RequestServices = scope.ServiceProvider`, and `await using` the scope for request lifetime (quickstart §5). Remove every `ShellContext` / `IShellHost.AcquireContextScope` reference
- [ ] T062 Rewrite `src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs` to iterate `registry.GetBlueprintNames().Select(name => registry.GetActive(name)).Where(s => s is not null)` and use `IShell.ServiceProvider` + `IShell.Descriptor` in place of `ShellContext.ServiceProvider` / `ShellContext.Id` / `ShellContext.Settings`. Remove the `CacheBackedShellHost` helper (no longer needed; the registry is the single source of truth)
- [ ] T063 Rewrite `src/CShells.AspNetCore/Resolution/FixedShellResolver.cs` similarly — takes a shell name and returns it, validated against `registry.GetActive(name)`
- [ ] T064 Update `src/CShells.AspNetCore/Extensions/` helpers — replace `IShellHost` parameters with `IShellRegistry`; replace `ShellContext` return values with `IShell`
- [ ] T065 Update `src/CShells.AspNetCore/Routing/ShellEndpointMetadata.cs` (or equivalent) — endpoint metadata continues to carry a `ShellId` (name only); no shape change
- [ ] T066 Remove any legacy shell-host DI wiring from `src/CShells.AspNetCore/` extension methods (`AddCShellsAspNetCore`, `UseCShells`)
- [ ] T067 [P] Update AspNetCore integration tests to use the new API (new middleware shape, `registry.GetActive`, `IShell`)
- [ ] T067a [P] End-to-end integration test for SC-010 in `tests/CShells.Tests/Integration/AspNetCore/InFlightRequestReloadTests.cs` (or the existing AspNetCore integration test project): spin up a `TestServer` with a shell whose middleware serves an endpoint that awaits a controllable signal (e.g., a `TaskCompletionSource`) while holding an `IShellScope`; start a request against generation N; trigger `registry.ReloadAsync("payments")`; assert the old generation enters `Draining` and its drain sits in phase 1 (scope wait); release the signal; assert the in-flight request returns 200 cleanly with no `ObjectDisposedException`, the old generation's provider is not disposed until the scope handle releases, and subsequent requests land on generation N+1 (SC-010)

**Checkpoint**: `grep -r IShellHost\|ShellContext\|IShellContextScope src/CShells.AspNetCore/`
returns zero hits.

---

## Phase 13: Migrate `CShells.FastEndpoints` + `CShells.AspNetCore.Testing` + `CShells.Providers.FluentStorage`

- [ ] T068 [P] Migrate `src/CShells.FastEndpoints/` and `src/CShells.FastEndpoints.Abstractions/` — replace every `IShellHost` / `ShellContext` reference with `IShellRegistry` / `IShell`; update any feature integration helpers
- [ ] T069 [P] Migrate `src/CShells.AspNetCore.Testing/` — update test helpers, fakes, and in-memory hosts to use `IShellRegistry` directly
- [ ] T070 [P] Migrate `src/CShells.Providers.FluentStorage/` — port the settings-provider logic from `IShellSettingsProvider` to an `IShellBlueprint` implementation (e.g., `FluentStorageShellBlueprint`) that re-reads from storage on every `ComposeAsync`; document how hosts register it via `cshells.RegisterBlueprint(new FluentStorageShellBlueprint(...))`

**Checkpoint**: Every downstream `src/` project compiles against the new surface.

---

## Phase 14: Migrate `samples/CShells.Workbench` and existing tests

- [ ] T071 Migrate `samples/CShells.Workbench/Program.cs` — move from `IShellSettingsProvider` / `IShellManager` to `cshells.AddShell(...)` blueprint registration; update `Background/ShellDemoWorker.cs` to call `registry.ReloadAsync` to showcase rolling rollover; add a sample `IShellInitializer` + `IDrainHandler` under `samples/CShells.Workbench.Features/Core/` to demonstrate both hooks; update `samples/CShells.Workbench/README.md` with a "Generations & drain demo" section
- [ ] T072 Migrate `samples/CShells.Workbench.Features/` — ensure the sample feature's `ConfigureServices` registers the new initializer + drain handler
- [ ] T073 Update every test under `tests/CShells.Tests/` that references legacy types. BEFORE editing, enumerate every legacy-test file (e.g., `grep -lE 'IShellHost|ShellContext|IShellManager|IShellActivatedHandler|IShellDeactivatingHandler|IShellSettingsProvider'`) and produce a migration table — one row per legacy test file — recording: current file path, target action (**Migrate** with the replacement test file / new assertions, or **Delete** with a one-sentence justification), and the new lifecycle behaviour it now covers (if any). Commit this table alongside the code changes (e.g., as a `test-migration.md` in the feature spec folder or in the PR description). Gate legacy-test deletion behind this table: a test may only be deleted if the table explicitly justifies why the legacy behaviour no longer needs coverage in the new API

**Checkpoint**: Everything in `src/` and `samples/` and `tests/` compiles; all tests that
remain are either passing or pending (with `[Fact(Skip = ...)]` explaining why) pending
deletion in Phase 15.

---

## Phase 15: Delete legacy types

**⚠️ PRECONDITION**: Phases 12–14 complete. Nothing in the repo compiles against the legacy
types anymore. A `grep -r <legacy-type-name>` across `src/`, `samples/`, and `tests/` must
return zero hits before each delete.

- [ ] T074 Delete `src/CShells.Abstractions/Management/` entirely (`IShellManager`, `IShellRuntimeStateAccessor`, `ShellReconciliationOutcome`, `ShellRuntimeStatus`) (FR-038)
- [ ] T075 [P] Delete `src/CShells.Abstractions/Hosting/IShellActivatedHandler.cs`, `Hosting/IShellDeactivatingHandler.cs`, `Hosting/ShellHandlerOrderAttribute.cs`
- [ ] T076 [P] Delete `src/CShells.Abstractions/Configuration/IShellSettingsProvider.cs`
- [ ] T077 [P] Delete `src/CShells/Management/` entirely (`DefaultShellManager`, `ShellRuntimeRecord`, `ShellRuntimeStateAccessor`, `ShellRuntimeStateStore`)
- [ ] T078 [P] Delete `src/CShells/Hosting/DefaultShellHost.cs`, `IShellHost.cs`, `IShellHostInitializer.cs`, `ShellContext.cs`, `IShellContextScope.cs`, `IShellContextScopeFactory.cs`, `DefaultShellContextScopeFactory.cs`, `ShellContextScopeHandle.cs`, `ShellCandidateBuildResult.cs`, `ShellStartupHostedService.cs`, `ShellFeatureInitializationHostedService.cs`
- [ ] T079 [P] Delete `src/CShells/Configuration/CompositeShellSettingsProvider.cs`, `ConfigurationShellSettingsProvider.cs`, `ConfiguringShellSettingsProvider.cs`, `InMemoryShellSettingsProvider.cs`, `MutableInMemoryShellSettingsProvider.cs`, `IShellSettingsCache.cs`, `ShellSettingsCache.cs`, `ShellSettingsCacheInitializer.cs`, `ShellSettingsFactory.cs`
- [ ] T080 Run `git grep -nE 'IShellHost|DefaultShellHost|IShellManager|DefaultShellManager|ShellContext\b|IShellContextScope|ShellContextScopeHandle|IShellRuntimeStateAccessor|ShellRuntimeRecord|ShellRuntimeStateStore|ShellReconciliationOutcome|ShellRuntimeStatus|IShellActivatedHandler|IShellDeactivatingHandler|ShellHandlerOrderAttribute|IShellSettingsProvider|CompositeShellSettingsProvider|ConfigurationShellSettingsProvider|ConfiguringShellSettingsProvider|InMemoryShellSettingsProvider|MutableInMemoryShellSettingsProvider|IShellSettingsCache|ShellSettingsCacheInitializer|ShellFeatureInitializationHostedService|ShellCandidateBuildResult|ShellStartupHostedService|ShellSettingsFactory|IShellHostInitializer'` across `src/`, `samples/`, `tests/` and confirm zero hits (SC-011)

**Checkpoint**: Legacy surface is gone. Repo builds cleanly on `net8.0;net9.0;net10.0`.

---

## Phase 16: Polish & Verification

- [ ] T081 [P] `dotnet build` — verify every library project builds cleanly on `net8.0`, `net9.0`, and `net10.0`
- [ ] T082 [P] `dotnet test` — run the full test suite and confirm every unit and integration lifecycle test passes
- [ ] T083 [P] Run the sample workbench locally and exercise the reload demo from T071; confirm structured log output shows the full state-transition sequence and populated `DrainResult` with `ScopeWaitElapsed`
- [ ] T084 Walk through `specs/006-shell-drain-lifecycle/quickstart.md` end-to-end against the implementation; fix any drift between the doc and the code
- [ ] T085 Review `Shell.cs`, `ShellScope.cs`, `ShellRegistry.cs`, `DrainOperation.cs`, blueprint implementations, and policy implementations for DRY opportunities, unnecessary comments, and any `lock()` around async paths (Principle VII); clean up before handoff
- [ ] T086 Update `CLAUDE.md` / top-level `README.md` if they still reference the legacy types or workflow

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no deps
- **Foundational (Phase 2)**: depends on Setup; BLOCKS every user story
- **US8 (Phase 3)**: depends on Foundational; must land before any other user-story phase because it owns `Shell` and the event bus
- **US1 + US4 (Phase 4)**: depends on Foundational + US8
- **US6 (Phase 5)**: depends on US1 (needs a `Shell` to scope)
- **US5 (Phase 6)**: depends on US6 (scope counter is drain phase 1 input)
- **US7 (Phase 7)**: depends on US5 (`DrainOperation` skeleton exists)
- **US2 (Phase 8)**: depends on US1 + US5 (reload both activates and drains)
- **US3 (Phase 9)**: depends on US2
- **US9 (Phase 10)**: depends on US5 (`IDrainPolicy` is resolved)
- **Startup/Shutdown (Phase 11)**: depends on US1 + US5
- **AspNetCore migration (Phase 12)**: depends on Phases 3–11 (needs the full new API)
- **Other migrations (Phases 13–14)**: depend on Phase 12 (for shared patterns) but each project can run in parallel
- **Legacy deletion (Phase 15)**: depends on Phases 12–14
- **Polish (Phase 16)**: depends on Phase 15

### User Story Dependencies

- US8 is the foundation: state machine + registry event bus + auto-logger.
- US1 + US4 add blueprints + activation with initializers on top of US8.
- US6 adds scope tracking that US5 consumes as drain phase 1.
- US5 adds drain handlers and DrainOperation phases 1–2.
- US7 completes DrainOperation with force + full result reporting.
- US2 stitches US1 + US5 + US6 into the headline reload flow.
- US3 wraps US2 for batch reload.
- US9 adds extensible/unbounded policies.

### Within Each User Story

- Tests (T017, T022–T025, T032, T035–T036, T041, T045–T046, T049, T051–T053, T058) are
  written first and must fail before implementation.
- Within US8: `Shell` skeleton (T018) → registry event bus (T019) → logger (T020) + DI
  wiring (T021) in parallel.
- Within US1/US4: blueprints (T026, T027) and registry blueprint methods (T028) can land in
  parallel; `ActivateAsync` (T029) depends on all; `GetActive`/`GetAll` (T030) and
  `AddShell` extension (T031) can land in parallel after T029.
- Within US6: scope impl (T033) and scope counter wiring (T034) must run sequentially (both
  touch `Shell.cs`).
- Within US5: policy (T037) and `DrainOperation` (T038) can start in parallel; registry
  `DrainAsync` (T039) depends on T038; DI default (T040) depends on T037.
- Within US7: T042–T044 all edit `DrainOperation.cs` and must run sequentially.
- Within US2: T047 depends on US5's `DrainAsync`; T048 is a branch inside T047.

### Parallel Opportunities

- Phase 2 foundational types T004–T015 are all in separate files — all marked [P].
- US1 blueprint tests (T022, T023) + activate integration tests (T024) + initializer
  integration tests (T025) can run alongside blueprint implementations (T026, T027).
- US9 policy implementations (T054, T055) are parallel; tests (T051–T053) are parallel.
- AspNetCore migration (Phase 12) and other-project migrations (Phase 13) are independent
  within themselves: T068, T069, T070 touch separate projects; T067 is a separate test set.
- Legacy-deletion tasks T075–T079 touch separate files/folders and can run in parallel.
- Polish verification tasks T081, T082, T083 run in parallel.

---

## Parallel Example: Foundational Phase

```bash
# Launch all foundational abstraction tasks together:
Task: "Create ShellLifecycleState enum"                                                   # T004
Task: "Create ShellDescriptor record"                                                     # T005
Task: "Create IShellScope interface"                                                      # T006
Task: "Create IShell interface"                                                           # T007
Task: "Create IShellBlueprint interface"                                                  # T008
Task: "Create IShellInitializer interface"                                                # T009
Task: "Create IShellLifecycleSubscriber"                                                  # T010
Task: "Create IDrainHandler + IDrainExtensionHandle"                                      # T011
Task: "Create IDrainPolicy"                                                               # T012
Task: "Create DrainStatus/DrainResult/DrainHandlerResult"                                 # T013
Task: "Create IDrainOperation"                                                            # T014
Task: "Create ReloadResult"                                                               # T015
```

## Parallel Example: Legacy Deletion

```bash
# Run after Phase 14 — all deletions target different files:
Task: "Delete Management/ abstractions"                                                    # T074
Task: "Delete Hosting handler abstractions"                                                # T075
Task: "Delete IShellSettingsProvider"                                                      # T076
Task: "Delete Management/ implementations"                                                 # T077
Task: "Delete Hosting/ legacy implementations"                                             # T078
Task: "Delete Configuration/ legacy settings-provider files"                               # T079
```

---

## Implementation Strategy

### MVP (US8 + US1 + US4 + US6 + US5 + US2 — all P1 plus their dependencies)

1. Phase 1 Setup
2. Phase 2 Foundational (CRITICAL — blocks all stories)
3. Phase 3 US8 — state machine + events + auto-logger
4. Phase 4 US1 + US4 — blueprints + activation with initializers
5. Phase 5 US6 — scopes + counter
6. Phase 6 US5 — drain handlers + scope-wait phase
7. Phase 8 US2 — `ReloadAsync` rollover
8. **STOP and VALIDATE**: All P1 stories pass their independent tests end-to-end.
9. Deploy/demo against the sample workbench (after the workbench migration in Phase 14)

At the MVP checkpoint, hosts can register blueprints, reload them to pick up configuration
changes, run drain handlers cooperatively while in-flight scopes finish, and observe every
generation's lifecycle via the auto-registered logger.

### Incremental Delivery

1. MVP as above (internal only; legacy surface still present but unused by new code)
2. Add US3 (`ReloadAllAsync`) → demo batch rollover
3. Add US7 (`ForceAsync` + full `DrainResult`) → demo operator observability
4. Add US9 (policies) → demo configuration options
5. Startup + shutdown (Phase 11)
6. Migrate AspNetCore (Phase 12) → headline downstream consumer works on new surface
7. Migrate remaining projects (Phases 13–14)
8. Delete legacy (Phase 15)
9. Polish (Phase 16) → ship

Legacy types remain present but unreferenced between Phase 11 and Phase 15; this keeps the
new code reviewable without a 5,000-line deletion diff overshadowing it.

### Parallel Team Strategy

After Foundational + US8:
- Developer A: US1/US4 → US2 → US3 → Phase 11 (activation + reload + startup/shutdown)
- Developer B: US6 → US5 → US7 (scopes + drain + result)
- Developer C: US9 → AspNetCore migration (policies, then downstream rewire)

---

## Notes

- [P] tasks touch different files with no cross-dependencies.
- `ShellId` is NOT modified — no breaking change to the name-only identity type.
- Every drain handler exception MUST surface as `DrainHandlerResult.Error`; never swallowed
  (Principle IV).
- No `lock()` around async paths — use `SemaphoreSlim` or `Interlocked.*` (Principle VII).
- Subscriber exceptions are caught, logged, and swallowed; they MUST NOT abort transitions
  or other subscribers.
- Generation numbers are library-owned: host code must never supply one to any lifecycle
  operation (FR-007).
- Multi-target build must stay green on `net8.0;net9.0;net10.0` for every library project
  change.
- Keep constructor/field/primary-constructor style modern per Principle III.
- Legacy deletion (Phase 15) is the single place where file removal happens; do not delete
  legacy files piecemeal during earlier phases — it makes review harder and risks breaking
  partially-migrated consumers.
