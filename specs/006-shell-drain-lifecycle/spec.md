# Feature Specification: Shell Draining & Disposal Lifecycle

**Feature Branch**: `006-shell-drain-lifecycle`
**Created**: 2026-04-22
**Status**: Draft
**Input**: User description: "CShell Shell Draining and Disposal Lifecycle"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Observe shell state transitions (Priority: P1)

A host application developer needs to know when a shell transitions between lifecycle states (Active → Deactivating → Draining → Drained → Disposed) so they can react appropriately — for example, pausing intake before triggering a drain.

**Why this priority**: Without observable state, host applications cannot coordinate graceful shutdown safely. This is the foundational capability on which all other stories depend.

**Independent Test**: Can be fully tested by creating a shell, advancing it through states programmatically, and asserting that state-change events fire in the correct order with the correct values.

**Acceptance Scenarios**:

1. **Given** a shell in Active state, **When** drain is initiated, **Then** the shell transitions through Deactivating → Draining → Drained → Disposed in order, and each transition fires a state-changed event.
2. **Given** a subscriber registered on a shell, **When** the shell transitions to a new state, **Then** the subscriber's handler is invoked with the old and new state values.
3. **Given** a shell that has reached Disposed, **When** any further state transition is attempted, **Then** no transition occurs and the shell remains Disposed.

---

### User Story 2 - Register drain handlers to complete in-flight work (Priority: P1)

A host developer registers one or more drain handlers on a shell's service collection. When that shell enters the Draining state, the handlers are invoked so the host can finish in-flight work (e.g., wait for running workflows to complete) before the service provider is disposed.

**Why this priority**: This is the primary integration point for host applications. Without drain handlers, CShell cannot offer cooperative disposal — the key stated goal.

**Independent Test**: Can be fully tested by registering a drain handler that records when it was called and awaits a short delay, triggering drain, and asserting the handler ran and was given a cancellation token.

**Acceptance Scenarios**:

1. **Given** a shell with a registered drain handler, **When** drain is initiated, **Then** the handler's drain method is invoked before the shell transitions to Drained.
2. **Given** multiple drain handlers registered on a shell, **When** drain is initiated, **Then** all handlers are invoked in parallel.
3. **Given** a drain handler that throws an exception, **When** drain completes, **Then** the drain result records the failure and the shell still transitions to Drained.
4. **Given** a drain handler, **When** the drain timeout elapses before the handler completes, **Then** the handler's cancellation token is cancelled and the shell transitions to Drained.

---

### User Story 3 - Replace an active shell while the old one drains (Priority: P2)

A host developer wants to deploy an updated shell (new version of services, new configuration) while the running shell finishes its in-flight work. The new shell becomes active immediately; the old shell drains cooperatively in the background.

**Why this priority**: The core motivation for the entire feature. Without it, replacement requires an abrupt, unsafe disposal.

**Independent Test**: Can be tested by creating two shells with the same name but different versions, calling Replace, and asserting the new shell is returned by `GetActive` while the old one is in Draining state.

**Acceptance Scenarios**:

1. **Given** an Active shell A for name "payments", **When** a new shell B for the same name is promoted, **Then** querying for the active shell returns B and shell A transitions to Deactivating.
2. **Given** shells A (Active) and B (already created), **When** `ReplaceAsync` is called with A and B, **Then** B becomes active and A drains asynchronously.
3. **Given** a consumer that captured a reference to shell A before replacement, **When** shell A is in Draining state, **Then** the consumer can still resolve services from A's provider.

---

### User Story 4 - Await drain completion and inspect results (Priority: P2)

A host developer that triggers a drain wants to await its completion and inspect which handlers completed, which timed out, and how long each took — for example, to emit structured logs or alert on slow drains.

**Why this priority**: Observability into drain results is necessary for operators to diagnose slow or failed drains in production.

**Independent Test**: Can be tested by triggering drain, awaiting completion, and asserting the returned result contains an entry for each registered handler with the correct status and elapsed time.

**Acceptance Scenarios**:

1. **Given** a drain in progress, **When** the drain completion is awaited, **Then** it resolves only after all handlers have completed or the timeout has elapsed.
2. **Given** a completed drain, **When** the result is inspected, **Then** it contains one entry per registered handler, each with a completed flag, optional error, and elapsed duration.
3. **Given** a drain in progress, **When** force-complete is called, **Then** all handler cancellation tokens are cancelled, the shell transitions to Drained promptly, and the result status is Forced.

---

### User Story 5 - Configure drain timeout policy (Priority: P3)

A host developer wants to control how long a drain waits for handlers to complete, and whether extension requests from handlers are honoured. They can set a fixed timeout, an extensible cap, or an unbounded wait for dev/test purposes.

**Why this priority**: Different environments require different timeout behaviour. Production needs safety bounds; local dev may want unbounded waits to avoid noise.

**Independent Test**: Can be tested by configuring a 1-second fixed timeout, registering a handler that waits indefinitely, triggering drain, and asserting the drain completes after approximately 1 second with a timed-out status.

**Acceptance Scenarios**:

1. **Given** a fixed-timeout policy with a 5-second limit, **When** handlers run longer than 5 seconds, **Then** drain is forced after 5 seconds and the result status is TimedOut.
2. **Given** an extensible-timeout policy, **When** a handler requests an extension, **Then** the deadline is extended up to the configured cap.
3. **Given** an unbounded policy, **When** drain starts, **Then** a warning is logged and drain waits indefinitely until all handlers complete.

---

### Edge Cases

- What happens when `CreateAsync` is called with a `ShellId` that already exists in the registry? An exception is thrown; duplicate registration is rejected.
- What happens when `CreateAsync` fails during shell construction (e.g., configure action throws)? The exception propagates to the caller and the shell is not registered; the registry remains unchanged.
- What happens when drain is initiated concurrently for the same shell? Only one drain operation is created; all callers receive the same in-flight handle.
- What happens when a shell is already Draining and promotion is attempted on it? Promotion is only valid for Active shells; the call fails with a clear error.
- What happens when two concurrent `PromoteAsync` calls target different shells with the same name? Both calls succeed in arrival order; each promotion serializes, and the last one to complete becomes the active shell.
- What happens when a drain handler resolves services from the shell being drained? It succeeds, because disposal only occurs after drain completes.
- What happens when `DisposeAsync` is called directly on a shell that has not been drained? The shell transitions immediately to Disposed, skipping the drain phase.
- What happens when no drain handlers are registered? Drain completes immediately with no handler results, transitioning through Draining → Drained without delay.
- What happens when `GetActive` is called for a name with no shells? Returns null.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST define the shell lifecycle states: Initializing, Active, Deactivating, Draining, Drained, Disposed.
- **FR-002**: Shell state transitions MUST be monotonic; a shell MUST NOT move backward through states.
- **FR-003**: Consumers MUST be able to subscribe to per-shell state-change events.
- **FR-004**: Consumers MUST be able to subscribe to global shell lifecycle events via the registry.
- **FR-005**: Host applications MUST be able to register drain handlers on a shell's service collection.
- **FR-006**: All registered drain handlers MUST be invoked in parallel when a shell enters Draining.
- **FR-007**: Each drain handler MUST receive a cancellation token that is cancelled when the drain deadline is reached or drain is forced.
- **FR-008**: Drain handlers MUST be able to request a deadline extension; the configured policy MUST decide whether to grant it.
- **FR-009**: The library MUST support configurable drain timeout policies, with a fixed-timeout policy as the default (30 seconds).
- **FR-010**: The library MUST allow multiple shells with the same name to coexist, with exactly one Active at a time.
- **FR-011**: The registry MUST provide a way to retrieve the single Active shell for a given name.
- **FR-012**: The registry MUST provide a way to retrieve all shells with a given name regardless of state.
- **FR-013**: Promoting a shell MUST atomically designate it as Active and transition the previous Active shell (if any) to Deactivating. Concurrent `PromoteAsync` calls for the same shell name MUST be serialized; both calls succeed in arrival order and the last one to complete becomes the active shell.
- **FR-014**: The library MUST provide a replace operation that promotes a new shell and initiates drain on the previous shell in a single call.
- **FR-015**: Callers MUST be able to await drain completion and receive a structured result including per-handler status and elapsed time.
- **FR-016**: Callers MUST be able to force-complete a drain at any time, cancelling outstanding handler tokens.
- **FR-017**: Shell descriptor metadata MUST be opaque to the library and surfaced unchanged in events and queries.
- **FR-018**: Concurrent drain calls for the same shell MUST return the same in-flight operation rather than starting a second drain.
- **FR-019**: `CreateAsync` MUST throw an exception if the registry already contains a shell with the same `ShellId` (name + version). Duplicate registration is a programming error and MUST be rejected loudly.
- **FR-020**: If `CreateAsync` fails for any reason (e.g., the configure action throws, or the service provider build fails), the exception MUST propagate to the caller and the shell MUST NOT be added to the registry. No partial or failed shell entry is retained.
- **FR-021**: The library MUST automatically register a structured-logging subscriber backed by `ILogger` when CShell is added to the host's service collection. This subscriber MUST emit a structured log entry for every shell lifecycle transition, including the shell descriptor metadata. No host configuration is required to activate it.

### Key Entities

- **Shell**: A named, versioned service container with an identity, lifecycle state, and built service provider. Terminal when Disposed.
- **Shell Descriptor**: Immutable identity and metadata snapshot created at shell creation time; includes name, version, creation timestamp, and an opaque metadata dictionary.
- **Shell Registry**: The authoritative collection of all shells, keyed by name and version; provides enumeration and global event subscription.
- **Drain Operation**: A handle representing an in-progress or completed drain; exposes status, deadline, progress, a completion awaitable, and force capability.
- **Drain Handler**: A host-registered callback resolved from the draining shell's service provider; performs graceful-shutdown work and returns when done.
- **Drain Policy**: A strategy that governs the initial timeout, extension decisions, and deadline-breach behaviour for a drain operation.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host application can replace an active shell and have all registered drain handlers complete before the service provider is disposed, with zero forced early disposals under normal load.
- **SC-002**: Drain handler registration, invocation, and result collection work correctly for shells with 0 to 50 registered handlers without special configuration.
- **SC-003**: Shell state-change events reach all registered subscribers within the same logical operation that caused the transition, with no events silently dropped under normal conditions.
- **SC-004**: Concurrent drain calls for the same shell always return the same operation handle; no duplicate drains are ever started.
- **SC-005**: Configuring a drain timeout of T seconds results in drain completing within T + G seconds, where G is the configurable cancellation grace period (default 3 seconds), under all built-in policy types. After force or timeout, the shell MUST reach Drained within G seconds regardless of handler behaviour.
- **SC-006**: All shell lifecycle transitions are observable via structured events carrying the shell descriptor metadata, enabling downstream logging and diagnostics without polling. The library emits these as structured log entries by default without any host configuration.
- **SC-007**: Shells with no registered drain handlers complete drain immediately and transition to Drained without delay, error, or special configuration.

---

## Clarifications

### Session 2026-04-22

- Q: What happens when `CreateAsync` is called with a `ShellId` that already exists in the registry? → A: Throw an exception; duplicate `ShellId` is rejected.
- Q: What happens when `CreateAsync` fails during shell construction? → A: Exception propagates to caller; shell is never added to the registry.
- Q: Does the library ship a default structured-logging subscriber or does the host wire its own? → A: Library registers a default `ILogger`-backed subscriber automatically; no host configuration required.
- Q: What happens when two concurrent `PromoteAsync` calls target different shells with the same name? → A: Serialized; both succeed in arrival order; last one wins as active.
- Q: What is the maximum time allowed between force/timeout and the shell reaching Drained? → A: Configurable grace period, default 3 seconds.

## Assumptions

- Shell version is a free-form string; the library does not interpret or compare versions semantically. Hosts that need semver can enforce it themselves.
- Drain handlers that do not complete within the configured timeout are considered timed out; their completed flag in the result is false.
- The cancellation grace period — the maximum time allowed between force/timeout and the shell reaching Drained — is configurable with a default of 3 seconds. It is separate from the main drain timeout.
- Drain handlers are registered as transient services resolved at drain time from the draining shell's provider.
- The registry holds shells until they are Disposed; callers are not responsible for lifecycle management beyond triggering drain.
- The unbounded policy is intended for development and test environments only; using it in production should produce a log warning.
