# Research: Shell Draining & Disposal Lifecycle

**Phase 0 output** — All NEEDS CLARIFICATION items from the Technical Context are resolved below.

---

## Decision 1 — ShellId breaking change: add Version

**Decision**: Extend the existing `ShellId` value record to carry both `Name` (string) and `Version`
(string). Equality is `(Name, Version)` case-insensitive.

**Why**: The spec defines `ShellId` as `(name, version)` throughout (FR-010, FR-013, FR-019).
The current `ShellId` is name-only. Carrying version in the identity type makes the uniqueness
constraint self-documenting and prevents callers from constructing ambiguous identifiers.

**Alternatives considered**:
- *Introduce a separate `VersionedShellId` type* — rejected; two identity types for the same
  concept create confusion. Constitution Principle VI explicitly permits breaking changes.
- *Use `ShellDescriptor` as the registry key* — rejected; `ShellDescriptor` includes timestamp and
  metadata that are irrelevant for identity comparison. A dedicated, lightweight value type is cleaner.

**How to apply**: `new ShellId("payments", "v2")`. The implicit `string → ShellId` conversion is
removed (it was name-only; the caller must now supply version explicitly). Callers using the old
single-argument constructor will receive a compile error — the intended migration signal.

---

## Decision 2 — State machine representation

**Decision**: Represent `ShellLifecycleState` as an `int` field on `Shell` and use
`Interlocked.CompareExchange` for atomic forward-only transitions. No locks required for state reads.

**Why**: The state enum maps directly to small sequential integers. CAS provides lock-free
monotonic advancement with correct memory ordering on all .NET platforms. Any attempt to move
backward will fail the CAS and return the current value, making it a safe no-op.

**Alternatives considered**:
- *`SemaphoreSlim(1,1)` around every state transition* — rejected; overkill for a simple forward-only
  state machine where reads are frequent and writes are rare.
- *`volatile` field + spin-wait* — rejected; CAS provides the atomicity guarantee without spinning.

---

## Decision 3 — Per-name promote serialization

**Decision**: `ShellRegistry` maintains a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by
shell name (case-insensitive). All `PromoteAsync` calls for the same name serialize through the
name's semaphore, satisfying FR-013's "concurrent PromoteAsync calls MUST be serialized" requirement.

**Why**: Serializing only within a name allows independent names to promote in parallel, which is
the common case. A single global semaphore would serialize unrelated promotions unnecessarily.

**Alternatives considered**:
- *Single `SemaphoreSlim` for all operations* — rejected; unnecessary global bottleneck for
  independent names.
- *`lock(nameString.Intern())` approach* — rejected; string interning is fragile and `lock()` around
  async paths violates Principle VII.

---

## Decision 4 — Idempotent concurrent drain

**Decision**: Use `Interlocked.CompareExchange` on a `DrainOperation?` field stored on `Shell` to
ensure exactly one `DrainOperation` is ever created per drain lifecycle. All concurrent `DrainAsync`
callers for the same shell receive the same `IDrainOperation` instance (FR-018, SC-004).

**Why**: CAS is the correct primitive for "create once, share" semantics. The first caller wins the
exchange; subsequent callers read the already-set reference.

**Alternatives considered**:
- *`SemaphoreSlim` guard around drain creation* — acceptable but heavier than CAS for a pure
  "create-once" pattern.
- *Lazy<T>* — rejected; `Lazy<T>` doesn't allow passing construction parameters or propagating
  cancellation.

---

## Decision 5 — Drain handler resolution

**Decision**: Drain handlers are resolved from the draining shell's `IServiceProvider` as
`IEnumerable<IDrainHandler>`. They are registered as **transient** services in
`IShellFeature.ConfigureServices`. `ShellRegistry.DrainAsync` resolves the collection immediately
before invoking handlers, so handlers can access their shell's services freely (FR-005, spec
assumption).

**Why**: Transient registration matches the "resolved at drain time" assumption from the spec.
Singleton handlers would hold shell-scoped state across drains (only one drain per shell lifetime,
but the pattern would be confusing). Scoped registration is irrelevant outside of request scopes.

**Alternatives considered**:
- *Registered as singleton* — rejected; transient is the documented assumption in the spec.
- *Dedicated `IDrainHandlerFactory`* — rejected; `IEnumerable<T>` resolution from DI is the
  idiomatic .NET pattern and requires no extra abstraction.

---

## Decision 6 — Drain completion awaitable

**Decision**: `DrainOperation` uses a `TaskCompletionSource<DrainResult>` internally. The public
`WaitAsync(CancellationToken)` wraps it with `await tcs.Task.WaitAsync(cancellationToken)`. The
`ForceAsync` path cancels all handler `CancellationTokenSource` instances and completes the TCS
with a `Forced` status once the grace period elapses.

**Why**: TCS is the standard .NET primitive for "complete a task from the outside." Channel would add
allocation and complexity without benefit for a single-producer, multi-consumer "done" signal.

**Alternatives considered**:
- *`ManualResetEventSlim` + `Task.Run`* — rejected; async-unfriendly.
- *`Channel<DrainResult>`* — rejected; a channel implies a sequence of values; drain produces
  exactly one result.

---

## Decision 7 — Grace period enforcement after force/timeout

**Decision**: After the drain deadline elapses or `ForceAsync` is called, the `DrainOperation`
cancels all handler `CancellationTokenSource` instances and starts a grace-period `CancellationToken`
linked to `CancellationTokenSource(gracePeriod)`. Handlers whose `Task` has not yet faulted or
completed after the grace period are treated as non-completing; the operation transitions the shell
to Drained regardless. This satisfies SC-005 (Drained within G seconds after force/timeout).

**Why**: The grace period gives handlers a bounded window to observe cancellation before hard
abandonment, without waiting indefinitely if a handler ignores the token.

**Alternatives considered**:
- *Await handlers with `Task.WhenAny(handlerTask, Task.Delay(grace))`* — equivalent; same approach
  expressed differently in code. Implementation will use this pattern per handler.

---

## Decision 8 — Drain timeout policies

**Decision**: Three concrete policy types, all implementing `IDrainPolicy`:
1. `FixedTimeoutDrainPolicy(TimeSpan timeout)` — default (30 s). `TryExtend` always returns false.
2. `ExtensibleTimeoutDrainPolicy(TimeSpan initial, TimeSpan cap)` — grants extensions up to cap.
3. `UnboundedDrainPolicy` — no deadline; logs a warning on first drain start (FR-009, story 5).

**Why**: The spec's user story 5 describes exactly these three modes. No fourth mode is implied.

**Alternatives considered**:
- *Callback-based `IDrainPolicy`* — more flexible but the spec doesn't call for it; premature
  extensibility violates Principle VI.
- *Single policy with flags* — harder to discover and test; three named types are clearer.

---

## Decision 9 — Logging subscriber auto-registration

**Decision**: `ShellLifecycleLogger` implements `IShellLifecycleSubscriber` and is registered as
a singleton during CShells DI setup (in `ServiceCollectionExtensions`). It subscribes itself to
the `ShellRegistry` during construction via `IShellRegistry.Subscribe`. No host configuration is
required (FR-021).

**Why**: The spec is explicit: "The library MUST automatically register a structured-logging
subscriber... No host configuration is required to activate it." Auto-registration in the DI setup
method is the standard CShells pattern for infrastructure services.

**Alternatives considered**:
- *`IHostedService` that subscribes on startup* — slightly deferred; a constructor-time subscription
  is simpler and guarantees no events are missed during host startup.

---

## Decision 10 — Relationship to existing IShellManager

**Decision**: `IShellRegistry` is a **parallel, independent API** that does not replace or depend
on `IShellManager`. Both can coexist. The existing reconciliation-based model (`IShellManager`,
`DefaultShellHost`) remains for configuration-driven shell management. `IShellRegistry` is the
code-first, lifecycle-explicit API introduced by this feature.

**Why**: The spec describes `IShellRegistry` as "the authoritative collection of all shells" for the
drain lifecycle feature. Coupling it to the existing settings reconciliation model would introduce
unnecessary complexity and risk regressions in the existing system.

**Alternatives considered**:
- *Replace `IShellManager` with `IShellRegistry`* — rejected for this feature; the settings-driven
  model serves a different use case (Orchard Core-style multi-tenancy reconciliation). A future
  feature could merge the two.
