# Research: Shell Generations, Reload & Disposal Lifecycle

**Phase 0 output** — All NEEDS CLARIFICATION items from the Technical Context are resolved below.

---

## Decision 1 — Identity: `ShellId` stays name-only; generation lives on the descriptor

**Decision**: `ShellId` remains a name-only value type with no breaking change. Generation is
exposed exclusively on `ShellDescriptor.Generation` (available via `IShell.Descriptor`). The
`(Name, Generation)` pair is never materialised as an equatable key anywhere in the API.

**Why**: The only legitimate needs for a composite key are (a) registry lookup and (b)
per-generation state. Case (a) is satisfied by name — there is at most one `Active` shell per
name, so callers look up by name and get back the currently-active `IShell`; draining older
generations are enumerated via `GetAll(name)`. Case (b) is satisfied by pinning state on the
`IShell` reference itself (e.g., a `DrainOperation?` slot is a field on `Shell`, not keyed by
a composite id). Removing the composite-key concept eliminates three smells at once:

1. The `ShellSettings` ordering puzzle — settings are composed before the generation is
   known, so a composite `ShellId` in settings would carry either a stale value or a
   sentinel. With name-only, `ShellSettings.Id` is simply the name.
2. The sentinel-generation compat hack for legacy code paths — not needed; `ShellId`
   semantics are unchanged.
3. Code accidentally comparing shells across generations via id equality — impossible, since
   only `IShell` references carry generation.

**Alternatives considered**:
- *Extend `ShellId` to `(Name, Generation)`* (earlier revision) — rejected. Forces a breaking
  change, requires a sentinel generation for legacy constructors, and introduces the
  settings-identity ordering issue for no real benefit.
- *Separate `ShellInstance` / `ShellHandle` id type* — rejected. No code needs it as an
  equatable key; it would be clutter.

**How to apply**: Diagnostics and structured logs format generation alongside name as
`"{Name}#{Generation}"` (via `ShellDescriptor.ToString()`), so observability keeps the full
identity visible without forcing a composite id type into the API.

---

## Decision 2 — Blueprint as the source of truth for activation and reload

**Decision**: Introduce `IShellBlueprint` with a single `ComposeAsync(CancellationToken)`
method plus a `Name` and optional `Metadata`. Blueprints are registered once per shell name
via `IShellRegistry.RegisterBlueprint` (or the fluent `AddShell(name, b => ...)` shorthand
that wraps a `DelegateShellBlueprint`). `ActivateAsync` and `ReloadAsync` invoke
`ComposeAsync` each time they run, producing a fresh `ShellSettings` that the registry
consumes to build the next generation.

**Why**: Reload must be blueprint-driven: hosts never supply a replacement `ShellSettings` —
they update the underlying source (fluent delegate captures, `Shells/*.json`, or an
`IConfiguration` section) and call `ReloadAsync(name)`. A single-method interface keeps the
abstraction minimal and lets us ship both fluent and config-backed implementations without
widening the contract (Principle VI). The blueprint's `Metadata` flows onto every
generation's `ShellDescriptor` — one source of truth, one-way flow.

**Alternatives considered**:
- *Pass a `Func<CancellationToken, Task<ShellSettings>>` instead of an interface* — rejected;
  the interface lets us carry `Name` and optional `Metadata` alongside the compose delegate.
- *Store a prebuilt `ShellSettings` instead of recomposing each reload* — rejected; the spec
  requires "re-composable on demand" so config-file edits or delegate captures updated
  between reloads are picked up.

---

## Decision 3 — Per-name reload serialization and generation assignment

**Decision**: `ShellRegistry` maintains a `ConcurrentDictionary<string, NameSlot>` keyed by
shell name (case-insensitive). Each `NameSlot` owns a `SemaphoreSlim(1, 1)`, a
`_nextGeneration` counter, the currently-`Active` `Shell`, and a list of non-active shells
still progressing through drain/disposal. `ActivateAsync` and `ReloadAsync` acquire the
slot's semaphore, increment the counter, compose via the blueprint, build the new shell as
`Initializing`, run its initializers, promote to `Active`, transition the previous `Active`
(if any) to `Deactivating`, release the semaphore, and kick off drain on the deposed
generation outside the lock.

**Why**: Serializing within a name (not globally) allows independent names to reload in
parallel — the common case for a multi-shell host. Holding the semaphore across
compose + build + initialize + promote guarantees FR-013: concurrent `ReloadAsync(name)`
calls complete in arrival order with strictly monotonic generation numbers. Starting drain
outside the lock keeps the critical section short and prevents a long-running drain from
blocking the next reload.

**Alternatives considered**:
- *Single global `SemaphoreSlim`* — rejected; unnecessary contention between unrelated shells.
- *`lock()` around the async compose + build steps* — rejected; violates Principle VII.
- *Optimistic generation assignment without a lock* — rejected; we need blueprint
  composition and provider build to observe a consistent "current active" when deciding
  whether there is a prior generation to drain.

**Generation failure handling**: if the counter is incremented but any of compose / build /
initialize throws, the generation number is simply "skipped" — the next successful reload
reads the next counter value. The partial provider (if built) is disposed. This satisfies
FR-005 (no reuse) and FR-014 (no partial entry) without bookkeeping.

---

## Decision 4 — State machine representation

**Decision**: Represent `ShellLifecycleState` as an `int` field on `Shell` and use
`Interlocked.CompareExchange` for atomic forward-only transitions. No locks required for
state reads.

**Why**: The state enum maps directly to small sequential integers. CAS provides lock-free
monotonic advancement with correct memory ordering on all .NET platforms. Any attempt to
move backward fails the CAS and returns the current value, making backward moves a safe
no-op (FR-018).

**Alternatives considered**:
- *`SemaphoreSlim(1,1)` around every state transition* — rejected; overkill for a simple
  forward-only state machine where reads are frequent and writes are rare.
- *`volatile` field + spin-wait* — rejected; CAS provides the atomicity guarantee without
  spinning.

---

## Decision 5 — Idempotent concurrent drain

**Decision**: Use `Interlocked.CompareExchange` on a `DrainOperation?` field stored on
`Shell` to ensure exactly one `DrainOperation` is ever created per drain lifecycle. All
concurrent `DrainAsync` callers for the same shell receive the same `IDrainOperation`
instance (FR-028, SC-006).

**Why**: CAS is the correct primitive for "create once, share" semantics. The first caller
wins the exchange; subsequent callers read the already-set reference.

**Alternatives considered**:
- *`SemaphoreSlim` guard around drain creation* — acceptable but heavier than CAS for a pure
  "create-once" pattern.
- *`Lazy<T>`* — rejected; `Lazy<T>` doesn't allow passing construction parameters or
  propagating cancellation.

---

## Decision 6 — Initializer semantics: sequential, in DI-registration order

**Decision**: `IShellInitializer` services are resolved from the newly-built shell's provider
as `IEnumerable<IShellInitializer>` and invoked **sequentially** in DI-registration order.
Any exception aborts activation, the partial provider is disposed, and the exception
propagates out of `ActivateAsync` / `ReloadAsync`.

**Why**: Initializers typically have ordering constraints — "set up the schema before the
seeder runs" — that are difficult to express through DI priority attributes. Sequential
resolution in the order `IServiceCollection` recorded them gives predictable behaviour
without additional metadata, matches how `IHostedService.StartAsync` already behaves, and
lets feature authors control ordering via the order their features' `ConfigureServices`
methods register the services.

Drain handlers, by contrast, run in parallel (FR-024) because they are cleanup work whose
interdependencies are the feature author's responsibility to manage. The asymmetry matches
the asymmetry of the two phases: activation is "set things up in order"; drain is
"everyone stop gracefully, as fast as possible."

**Alternatives considered**:
- *Parallel initializers* — rejected; hidden ordering constraints would bite.
- *`OrderAttribute` / priority hint* — rejected; DI-order is simpler and the legacy
  `ShellHandlerOrderAttribute` has not earned its complexity.

---

## Decision 7 — Drain handler resolution

**Decision**: Drain handlers are resolved from the draining shell's `IServiceProvider` as
`IEnumerable<IDrainHandler>`. They are registered as **transient** services in
`IShellFeature.ConfigureServices`. `DrainOperation` resolves the collection immediately
before invoking handlers, so handlers can access their shell's services freely (spec
assumption).

**Why**: Transient registration matches the "resolved at drain time" assumption.
Singleton handlers would hold shell-scoped state across drains (only one drain per shell
lifetime, but the pattern would be confusing). Scoped registration is irrelevant outside
request scopes.

**Alternatives considered**:
- *Registered as singleton* — rejected; transient is the documented assumption.
- *Dedicated `IDrainHandlerFactory`* — rejected; `IEnumerable<T>` resolution from DI is the
  idiomatic .NET pattern and requires no extra abstraction.

---

## Decision 8 — Drain completion awaitable

**Decision**: `DrainOperation` uses a `TaskCompletionSource<DrainResult>` internally. The
public `WaitAsync(CancellationToken)` wraps it with `await tcs.Task.WaitAsync(ct)`. The
`ForceAsync` path cancels all handler `CancellationTokenSource` instances and completes the
TCS with a `Forced` status once the grace period elapses.

**Why**: TCS is the standard .NET primitive for "complete a task from the outside." Channel
would add allocation and complexity without benefit for a single-producer, multi-consumer
"done" signal.

**Alternatives considered**:
- *`ManualResetEventSlim` + `Task.Run`* — rejected; async-unfriendly.
- *`Channel<DrainResult>`* — rejected; a channel implies a sequence of values; drain produces
  exactly one result.

---

## Decision 9 — Grace period enforcement after force/timeout

**Decision**: After the drain deadline elapses or `ForceAsync` is called, the
`DrainOperation` cancels all handler `CancellationTokenSource` instances and starts a
grace-period `CancellationToken` linked to `CancellationTokenSource(gracePeriod)`. Handlers
whose `Task` has not yet faulted or completed after the grace period are treated as
non-completing; the operation transitions the shell to `Drained` regardless. This satisfies
SC-007 (Drained within G seconds after force/timeout).

**Why**: The grace period gives handlers a bounded window to observe cancellation before
hard abandonment, without waiting indefinitely if a handler ignores the token.

**Alternatives considered**:
- *Await handlers with `Task.WhenAny(handlerTask, Task.Delay(grace))`* — equivalent; same
  approach expressed differently in code. Implementation will use this pattern per handler.

---

## Decision 10 — Drain timeout policies

**Decision**: Three concrete policy types, all implementing `IDrainPolicy`:
1. `FixedTimeoutDrainPolicy(TimeSpan timeout)` — default (30 s). `TryExtend` always returns
   false.
2. `ExtensibleTimeoutDrainPolicy(TimeSpan initial, TimeSpan cap)` — grants extensions up to
   cap.
3. `UnboundedDrainPolicy` — no deadline; logs a warning on first drain start.

**Why**: User story 9 describes exactly these three modes. No fourth mode is implied.

**Alternatives considered**:
- *Callback-based `IDrainPolicy`* — more flexible but the spec doesn't call for it; premature
  extensibility violates Principle VI.
- *Single policy with flags* — harder to discover and test; three named types are clearer.

---

## Decision 11 — Scope tracking as drain phase 1

**Decision**: `IShell.BeginScope()` returns an `IShellScope` handle that (a) creates an
`IServiceScope` from the shell's provider and (b) increments an active-scope counter on the
shell. Disposal decrements the counter and disposes the DI scope. Drain's **first phase**,
inside `DrainOperation`, awaits the counter reaching zero — bounded by the overall drain
deadline — before invoking any `IDrainHandler`. This is hard-coded as part of what "drain"
means; hosts do not configure it and cannot remove it.

**Why**: The existing `DefaultShellHost.AcquireContextScope` mechanism exists exactly because
under load, reloads would otherwise dispose the old provider out from under in-flight
requests, producing `ObjectDisposedException`s. The new architecture must preserve that
guarantee. Making scope-wait a first-class drain phase (rather than an opt-in scope tracker
or an auto-registered built-in drain handler) means:

- Hosts don't need to wire anything — `BeginScope` does both jobs.
- The guarantee can never be removed by a host that accidentally deletes an auto-registered
  service.
- The scope wait is observable in `DrainResult.ScopeWaitElapsed` for diagnostics.

The deadline bound on the scope-wait phase prevents a stuck scope from stalling drain
forever — if a scope handle is still held at the deadline, it is abandoned (not forcibly
disposed; drain proceeds to handler invocation and eventually to `Drained` regardless).

**Alternatives considered**:
- *Auto-registered built-in `IDrainHandler` that waits on the counter* — rejected. A host
  could remove or replace it; the behaviour would silently degrade. Making it a phase keeps
  the guarantee non-negotiable.
- *Forcibly dispose outstanding scopes at the deadline* — rejected. If a request is in the
  middle of a database transaction, forcible disposal still produces
  `ObjectDisposedException`s inside user code. Better to let the request finish naturally
  against the not-yet-disposed provider while drain proceeds.

**Implementation**: Scope counter is a single `int` on `Shell`, updated with
`Interlocked.Increment` / `Decrement`. A `TaskCompletionSource<int>` or manual-reset event
wakes the drain phase-1 waiter whenever the counter drops. The phase-1 waiter uses
`Task.WhenAny(counterDrop, Task.Delay(deadline))` to enforce the bound.

---

## Decision 12 — Logging subscriber auto-registration

**Decision**: `ShellLifecycleLogger` implements `IShellLifecycleSubscriber` and is registered
as a singleton during CShells DI setup (in `ServiceCollectionExtensions`). It subscribes
itself to the `ShellRegistry` during construction via `IShellRegistry.Subscribe`. No host
configuration is required (FR-034).

**Why**: The spec is explicit: "The library MUST automatically register a structured-logging
subscriber... No host configuration is required to activate it." Auto-registration in the DI
setup method is the standard CShells pattern for infrastructure services.

Subscriber exceptions are caught, logged, and swallowed (Principle VII) so they cannot block
other subscribers or the state transition itself.

**Alternatives considered**:
- *`IHostedService` that subscribes on startup* — slightly deferred; a constructor-time
  subscription is simpler and guarantees no events are missed during host startup.

---

## Decision 13 — Clean overhaul: no coexistence with legacy types

**Decision**: Remove the entire legacy hosting / management / settings-provider surface
(`IShellHost`, `DefaultShellHost`, `IShellManager`, `DefaultShellManager`, `ShellContext`,
`IShellContextScope*`, `IShellRuntimeStateAccessor`, `ShellRuntime*`, `IShellSettingsProvider`
implementations, `ShellSettingsFactory`, `IShellSettingsCache*`, `IShellActivatedHandler`,
`IShellDeactivatingHandler`, `ShellHandlerOrderAttribute`,
`ShellFeatureInitializationHostedService`, `ShellStartupHostedService`) and migrate every
downstream consumer (`CShells.AspNetCore`, `CShells.FastEndpoints`,
`CShells.AspNetCore.Testing`, `CShells.Providers.FluentStorage`, samples, tests) to the new
`IShellRegistry` / `IShell` surface in this feature.

**Why**: Coexistence would double the concept surface area. Hosts would face two ways to
start shells, two identity models, two drain patterns, two test conventions. The feature
owner explicitly opted into a clean overhaul. The migration surface is bounded — most of
the AspNetCore code paths map 1:1 to the new API, and the savings in maintenance and
teaching burden are significant.

**Migration map**:

| Legacy | Replacement |
|--------|-------------|
| `IShellHost.GetShell(id)` | `IShellRegistry.GetActive(name)` |
| `IShellHost.AllShells` | `registry.GetBlueprintNames().Select(registry.GetActive).Where(...)` |
| `IShellHost.AcquireContextScope(ctx)` | `IShell.BeginScope()` |
| `ShellContext` | `IShell` (+ `IShell.Descriptor` for identity/metadata) |
| `IShellContextScope` | `IShellScope` |
| `IShellManager.AddShellAsync(settings)` | `registry.RegisterBlueprint(bp)` + `registry.ActivateAsync(name)` |
| `IShellManager.ReloadShellAsync(id)` | `registry.ReloadAsync(name)` |
| `IShellManager.ReloadAllShellsAsync()` | `registry.ReloadAllAsync()` |
| `IShellActivatedHandler` | `IShellInitializer` |
| `IShellDeactivatingHandler` | `IDrainHandler` |
| `IShellSettingsProvider` + caches | `IShellBlueprint` + `ConfigurationShellBlueprint` |
| `ShellStartupHostedService` | `CShellsStartupHostedService` (new, simpler) |

**Alternatives considered**:
- *Keep legacy as a thin compat shim on top of the new API* — rejected. It doubles the test
  surface, keeps legacy terminology in IDE autocomplete for years, and slows adoption of
  the new model. A clean break is less work in aggregate.
- *Phase the overhaul: kill legacy in a follow-up feature* — rejected. Shipping two APIs
  simultaneously and then a third (the merge) is the worst of both worlds.

---

## Decision 14 — Shutdown drain

**Decision**: A built-in hosted service (`CShellsStartupHostedService` or equivalent)
subscribes to `IHostApplicationLifetime.ApplicationStopping` and calls `DrainAsync` on every
currently-`Active` shell in parallel using the configured `IDrainPolicy`. It awaits the
resulting drain operations bounded by the host's shutdown timeout. Any shells not fully
drained by shutdown timeout are disposed anyway so the host can actually exit (FR-036).

**Why**: Graceful host shutdown is a first-class spec requirement. Doing it inside the
library (rather than leaving it to the host) keeps the guarantee uniform regardless of how
the host is wired.

**Alternatives considered**:
- *Rely on `IServiceProvider` disposal order alone* — rejected; that disposes providers
  without running drain handlers, violating FR-036.
- *Skip disposal on shutdown-timeout breach* — rejected; a host that never exits on Ctrl-C
  is worse than one that skips some cleanup.
