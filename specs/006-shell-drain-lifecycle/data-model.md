# Data Model: Shell Generations, Reload & Disposal Lifecycle

**Phase 1 output**

---

## Entities

### ShellId *(unchanged)*

**Location**: `CShells.Abstractions` — `ShellId.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | Case-insensitive shell name |

**Equality**: case-insensitive on `Name`.

**Notes**: `ShellId` remains a name-only value type. Generation is **not** part of identity
and is never observable through `ShellId`. The `(Name, Generation)` pair is never needed as an
equatable key — callers look up shells by name and observe generation via
`IShell.Descriptor.Generation`.

---

### ShellDescriptor *(new)*

**Location**: `CShells.Abstractions/Lifecycle/ShellDescriptor.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | Shell name |
| `Generation` | `int` | Library-assigned monotonic counter, 1-based |
| `CreatedAt` | `DateTimeOffset` | UTC timestamp at shell creation |
| `Metadata` | `IReadOnlyDictionary<string, string>` | Sourced from the blueprint's `Metadata`; empty by default |

**Constraints**:
- Immutable record.
- `Name` non-null / non-whitespace; `Generation >= 1`.
- `Metadata` is never null; defaults to `ImmutableDictionary<string, string>.Empty`.
- Formatted as `"{Name}#{Generation}"` in `ToString()` for structured log fields.

---

### ShellLifecycleState *(new)*

**Location**: `CShells.Abstractions/Lifecycle/ShellLifecycleState.cs`

```
Initializing → Active → Deactivating → Draining → Drained → Disposed
```

| Value | Meaning |
|-------|---------|
| `Initializing` | Shell is being constructed; provider built; initializers running |
| `Active` | Shell is promoted; provider available; eligible for scopes |
| `Deactivating` | Shell has been superseded by a newer generation; transitioning to drain |
| `Draining` | Scope-wait + drain handlers running; provider still available |
| `Drained` | All handlers completed (or timed out); provider still valid until disposal |
| `Disposed` | `IServiceProvider` has been disposed; terminal state |

**Transitions allowed**:

| From | To | Trigger |
|------|-----|---------|
| `Initializing` | `Active` | Internal: after initializers succeed inside `ActivateAsync` / `ReloadAsync` |
| `Active` | `Deactivating` | Internal: a newer generation is promoted for the same name |
| `Active` | `Draining` | `DrainAsync` called directly (no replacement) |
| `Deactivating` | `Draining` | Internal lifecycle engine starts drain |
| `Draining` | `Drained` | Scope-wait + all handlers complete, time out, or drain is forced |
| `Drained` | `Disposed` | Registry disposes the provider after drain completes (normal path) |
| `Any non-terminal` | `Disposed` | Registry emergency-disposes on host shutdown timeout breach (FR-036); ONLY path that bypasses `Drained` |

**Rules**:
- Transitions are monotonic; backward moves are no-ops.
- Internal promote (used by `ActivateAsync` / `ReloadAsync`) only fires when initializers
  have completed successfully.
- There is no public disposal entry point on `IShell`; disposal is registry-owned (FR-037).
  The registry's own emergency-dispose path on shutdown-timeout breach is the only way a
  shell can reach `Disposed` without first reaching `Drained`.

---

### IShellBlueprint *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShellBlueprint.cs`

| Member | Type | Notes |
|--------|------|-------|
| `Name` | `string` | The shell name this blueprint is registered for |
| `Metadata` | `IReadOnlyDictionary<string, string>` | Static descriptor metadata (owner, tags); flows onto every generation's `ShellDescriptor` unchanged |
| `Task<ShellSettings> ComposeAsync(CancellationToken ct)` | | Produces a fresh `ShellSettings` on each invocation — called for every `ActivateAsync` / `ReloadAsync` |

**Built-in implementations** (in `CShells/Lifecycle/Blueprints/`):

| Type | Source | Behaviour |
|------|--------|-----------|
| `DelegateShellBlueprint` | Fluent (`AddShell(name, b => ...)`) | Invokes a stored `Action<ShellBuilder>` against a fresh `ShellBuilder(name)` on each compose |
| `ConfigurationShellBlueprint` | Config-backed | Re-reads the bound `IConfigurationSection` (or named `ShellConfig`) on each compose, so edits to the underlying source are picked up on reload |

**Constraints**:
- `ComposeAsync` MUST be re-invocable and MUST NOT mutate shared external state.
- `ComposeAsync` MAY be async (IO-bound config reads) but should complete promptly.
- If composition throws, the exception propagates out of `ActivateAsync` / `ReloadAsync`
  (FR-014).
- The returned `ShellSettings` MUST carry `Name` matching the blueprint; the registry
  validates this and throws `InvalidOperationException` on mismatch.

---

### IShellScope *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShellScope.cs`

| Member | Type | Notes |
|--------|------|-------|
| `Shell` | `IShell` | The owning shell |
| `ServiceProvider` | `IServiceProvider` | A DI scope built from `Shell.ServiceProvider` |
| `DisposeAsync` | | Releases the DI scope and decrements the shell's active-scope counter |

**Behaviour**:
- Obtained via `IShell.BeginScope()`.
- Outstanding scopes delay drain-handler invocation (FR-022). Drain's scope-wait phase
  completes once every `IShellScope` obtained from the shell has been disposed, or the drain
  deadline elapses — whichever comes first.
- Scope handles outstanding at the drain deadline are not forcibly disposed; drain proceeds
  to handler invocation with the cancelled token and the handles dispose naturally when
  their callers eventually release them.
- Thread-safe: multiple concurrent callers can `BeginScope` on the same shell.

---

### IShell *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShell.cs`

| Member | Type | Notes |
|--------|------|-------|
| `Descriptor` | `ShellDescriptor` | Immutable identity + metadata (includes generation) |
| `State` | `ShellLifecycleState` | Current lifecycle state; changes atomically |
| `ServiceProvider` | `IServiceProvider` | Resolvable until `Disposed` |
| `BeginScope()` | `IShellScope` | Creates a tracked DI scope |

**Does NOT** implement `IDisposable` / `IAsyncDisposable`. Shell disposal is entirely
registry-owned (FR-037); the `Shell` implementation has an `internal` async-disposal method
the registry calls after drain completes, or as part of the emergency-shutdown path (FR-036).

---

### IShellInitializer *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShellInitializer.cs`

| Member | Notes |
|--------|-------|
| `Task InitializeAsync(CancellationToken cancellationToken)` | Called once during `Initializing → Active`. |

**Registration**: Registered in the shell's `IServiceCollection` via
`IShellFeature.ConfigureServices`. Resolved at activation time via
`IEnumerable<IShellInitializer>` from the newly-built shell's provider.

**Semantics**:
- Initializers run sequentially in DI-registration order (FR-016).
- If any initializer throws, the exception propagates out of `ActivateAsync` /
  `ReloadAsync`; the shell never reaches `Active`; its provider is disposed; no partial
  entry is retained (FR-014).

---

### IDrainHandler *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainHandler.cs`

| Member | Notes |
|--------|-------|
| `Task DrainAsync(IDrainExtensionHandle extensionHandle, CancellationToken cancellationToken)` | Called when shell enters `Draining`, after the scope-wait phase. Token is cancelled at deadline. |

**Registration**: Registered as transient in the shell's `IServiceCollection` via
`IShellFeature.ConfigureServices`. Resolved at drain time via `IEnumerable<IDrainHandler>`
from the draining generation's provider.

---

### IDrainExtensionHandle *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainExtensionHandle.cs`

| Member | Notes |
|--------|-------|
| `bool TryExtend(TimeSpan requested, out TimeSpan granted)` | Ask the policy for a deadline extension. Returns `false` if policy rejects. |

---

### IDrainPolicy *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainPolicy.cs`

| Member | Notes |
|--------|-------|
| `TimeSpan? InitialTimeout` | `null` means unbounded |
| `bool IsUnbounded` | `true` for `UnboundedDrainPolicy` |
| `bool TryExtend(TimeSpan requested, out TimeSpan granted)` | Called by `IDrainExtensionHandle`; fixed/unbounded always return `false` / `true` respectively |

**Concrete implementations** (in `CShells/Lifecycle/Policies/`):

| Type | Behaviour |
|------|-----------|
| `FixedTimeoutDrainPolicy(TimeSpan timeout)` | Default; `TryExtend` always returns `false` |
| `ExtensibleTimeoutDrainPolicy(TimeSpan initial, TimeSpan cap)` | Grants extensions cumulatively up to cap |
| `UnboundedDrainPolicy` | No deadline; logs a warning on first use; `TryExtend` grants anything |

---

### IDrainOperation *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainOperation.cs`

| Member | Type | Notes |
|--------|------|-------|
| `Status` | `DrainStatus` | `Pending` / `Completed` / `TimedOut` / `Forced` |
| `Deadline` | `DateTimeOffset?` | Null when policy is unbounded |
| `Task<DrainResult> WaitAsync(CancellationToken)` | | Awaitable; resolves when drain finishes |
| `Task ForceAsync(CancellationToken)` | | Cancels all handler tokens; transitions shell to `Drained` promptly |

**DrainStatus** (enum, in `DrainResult.cs`):

| Value | Meaning |
|-------|---------|
| `Pending` | Drain in progress |
| `Completed` | All handlers finished within deadline |
| `TimedOut` | Deadline elapsed; handlers cancelled |
| `Forced` | `ForceAsync` was called |

**Drain phases** *(implementation semantics)*:

1. **Scope wait** — await the shell's active-scope counter reaching zero, bounded by the
   drain deadline. No handler runs during this phase. If the deadline elapses first, proceed
   to phase 2 with the cancelled token; outstanding scopes are abandoned.
2. **Handler invocation** — resolve `IEnumerable<IDrainHandler>` from the shell's provider
   and invoke all handlers in parallel. Each handler receives an extension handle and a
   cancellation token linked to the remaining deadline budget.
3. **Grace** — after deadline or `ForceAsync`, wait up to the grace period (default 3 s) for
   handlers to observe cancellation. Transition to `Drained` regardless of remaining handler
   state.

---

### DrainResult *(new)*

**Location**: `CShells.Abstractions/Lifecycle/DrainResult.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Shell` | `ShellDescriptor` | The drained shell (carries generation) |
| `Status` | `DrainStatus` | Overall outcome |
| `ScopeWaitElapsed` | `TimeSpan` | How long phase 1 took |
| `AbandonedScopeCount` | `int` | Scope handles still outstanding when phase 1 ended (non-zero only when phase 1 was bounded out by the deadline) |
| `HandlerResults` | `IReadOnlyList<DrainHandlerResult>` | One entry per registered handler |

---

### DrainHandlerResult *(new)*

**Location**: `CShells.Abstractions/Lifecycle/DrainHandlerResult.cs`

| Field | Type | Notes |
|-------|------|-------|
| `HandlerTypeName` | `string` | `handler.GetType().Name` |
| `Completed` | `bool` | True if handler returned within deadline |
| `Elapsed` | `TimeSpan` | Wall-clock time consumed by this handler |
| `Error` | `Exception?` | Non-null if handler threw |

---

### ReloadResult *(new)*

**Location**: `CShells.Abstractions/Lifecycle/ReloadResult.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | Shell name |
| `NewShell` | `IShell?` | The newly-activated generation; null if composition or activation failed |
| `Drain` | `IDrainOperation?` | Drain operation on the previous generation; null if there was none or composition failed |
| `Error` | `Exception?` | Non-null if blueprint composition, provider build, or initializer threw |

Used as the return type of individual `ReloadAsync` calls and as the element type of the
collection returned by `ReloadAllAsync`.

---

### IShellLifecycleSubscriber *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShellLifecycleSubscriber.cs`

| Member | Notes |
|--------|-------|
| `Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken)` | Called for every state transition on any shell in the registry |

**Isolation**: Subscriber exceptions are caught, logged, and swallowed so they cannot block
other subscribers or the state transition itself.

---

### IShellRegistry *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShellRegistry.cs`

| Member | Signature | Notes |
|--------|-----------|-------|
| `RegisterBlueprint` | `void RegisterBlueprint(IShellBlueprint blueprint)` | Registers a blueprint for a name. Throws `InvalidOperationException` on duplicate (FR-003). |
| `GetBlueprint` | `IShellBlueprint? GetBlueprint(string name)` | Looks up the registered blueprint for a name; null if none. |
| `GetBlueprintNames` | `IReadOnlyCollection<string> GetBlueprintNames()` | Enumerates every registered name. |
| `ActivateAsync` | `Task<IShell> ActivateAsync(string name, CancellationToken ct)` | Composes via blueprint, builds generation 1, runs initializers, promotes to `Active` (FR-009). |
| `ReloadAsync` | `Task<ReloadResult> ReloadAsync(string name, CancellationToken ct)` | Composes fresh settings, builds generation N+1, runs initializers, promotes, drains previous (FR-010). If no active generation exists, behaves like `ActivateAsync` (FR-011). |
| `ReloadAllAsync` | `Task<IReadOnlyList<ReloadResult>> ReloadAllAsync(CancellationToken ct)` | Reloads every registered blueprint; per-name results (FR-012). |
| `DrainAsync` | `Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken ct)` | Explicit cooperative drain on a specific shell instance. Concurrent calls return the same operation (FR-028). |
| `GetActive` | `IShell? GetActive(string name)` | The currently `Active` shell for `name`; null if none (FR-031). |
| `GetAll` | `IReadOnlyCollection<IShell> GetAll(string name)` | All generations currently held for `name` regardless of state (FR-032). |
| `Subscribe` | `void Subscribe(IShellLifecycleSubscriber subscriber)` | Thread-safe subscriber registration. |
| `Unsubscribe` | `void Unsubscribe(IShellLifecycleSubscriber subscriber)` | Thread-safe subscriber removal. |

Activate and reload compose + build + initialize + promote as a single unit of work; those
phases are not separately callable.

---

## Validation Rules

| Rule | Enforcement |
|------|-------------|
| Blueprint name must be unique | `RegisterBlueprint` throws `InvalidOperationException` (FR-003) |
| `ActivateAsync` / `ReloadAsync` require a registered blueprint | Throws `InvalidOperationException` with "No blueprint registered for name '{name}'" |
| Blueprint-produced `ShellSettings.Id.Name` matches the blueprint name | `ActivateAsync` / `ReloadAsync` throws `InvalidOperationException` on mismatch |
| Blueprint composition / provider build / initializer exceptions propagate | No partial generation retained; partial provider disposed (FR-014) |
| Concurrent reloads for the same name are serialized | Per-name `SemaphoreSlim(1,1)`; generation numbers assigned in acquire order (FR-013) |
| State transitions are monotonic | CAS on `_state`; backward attempts are no-ops |
| Handler exceptions do not abort drain | Caught; stored in `DrainHandlerResult.Error` |
| Subscriber exceptions do not abort state transitions | Caught, logged, swallowed |
| Outstanding scopes delay drain-handler invocation | Scope-wait phase bounded by drain deadline (FR-022) |

---

## State Transition Diagram

```
                           (per generation)

          internal Promote
  [Initializing] ─────────────────► [Active]
                                       │
                              ┌────────┴────────┐
                      reload  │                 │ direct
                   supersedes │                 │ DrainAsync
                              ▼                 │
                        [Deactivating]          │
                              │                 │
                              ▼                 ▼
                           [Draining] ◄─────────┘
                              │
                 (1) scope-wait phase
                 (2) handler invocation
                 (3) grace after deadline / force
                              │
                              ▼
                           [Drained]
                              │
                              ▼
                          [Disposed] ◄── registry emergency-dispose (from any state, shutdown only)
```

The diagram applies to a single generation. A reload produces a new generation alongside the
old one; both progress through the diagram independently (new goes `Initializing → Active`,
old goes `Active → Deactivating → Draining → Drained → Disposed`).

---

## Generation Numbering

| Rule | Notes |
|------|-------|
| Start value | `1` on first `ActivateAsync` for a name |
| Increment | `+1` on each successful `ReloadAsync` step inside the name's serialization lock |
| Uniqueness | Unique per `(Name)` within the process lifetime — never reused, even on failure |
| Failure handling | If composition, build, or initializer fails after the number is assigned, that number is **skipped**; the next successful reload uses the next available value |
| Visibility | Read-only via `shell.Descriptor.Generation`; never mutable |
| Storage | Per-name counter held on the `ShellRegistry`'s name-slot record; increments happen under the per-name semaphore |
