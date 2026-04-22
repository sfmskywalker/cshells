# Data Model: Shell Draining & Disposal Lifecycle

**Phase 1 output**

---

## Entities

### ShellId *(breaking change to existing type)*

**Location**: `CShells.Abstractions` — `ShellId.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | Case-insensitive shell name |
| `Version` | `string` | Free-form version string; not interpreted semantically |

**Equality**: `(Name, Version)` — both compared case-insensitively.

**Validation**: Both `Name` and `Version` must be non-null, non-whitespace (guard at constructor).

**Breaking change**: The implicit `string → ShellId` conversion and single-argument constructor are
removed. Callers must supply `new ShellId("name", "version")`. The `Name`-only constructor used by
the existing `IShellManager`/`DefaultShellHost` path will be updated to supply a sentinel version
(e.g. `"__unversioned__"`) so the existing code continues to compile; this is an internal concern
and transparent to users of `IShellRegistry`.

---

### ShellDescriptor *(new)*

**Location**: `CShells.Abstractions/Lifecycle/ShellDescriptor.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | Shell name — matches `ShellId.Name` |
| `Version` | `string` | Shell version — matches `ShellId.Version` |
| `CreatedAt` | `DateTimeOffset` | UTC timestamp at shell creation |
| `Metadata` | `IReadOnlyDictionary<string, string>` | Opaque host-supplied metadata; empty by default |

**Constraints**:
- Immutable record.
- `Name` and `Version` are non-null, non-whitespace.
- `Metadata` is never null; defaults to `ImmutableDictionary<string, string>.Empty`.
- `ShellId` is derivable: `new ShellId(Name, Version)`.

**State transitions**: N/A — descriptor is created once and never mutated.

---

### ShellLifecycleState *(new)*

**Location**: `CShells.Abstractions/Lifecycle/ShellLifecycleState.cs`

```
Initializing → Active → Deactivating → Draining → Drained → Disposed
```

| Value | Meaning |
|-------|---------|
| `Initializing` | Shell is being constructed; service provider not yet ready |
| `Active` | Shell is promoted; service provider available; eligible for requests |
| `Deactivating` | Shell has been superseded; transitioning to drain |
| `Draining` | Drain handlers are running; service provider still available |
| `Drained` | All handlers completed (or timed out); provider still valid until disposal |
| `Disposed` | `IServiceProvider` has been disposed; terminal state |

**Transitions allowed**:

| From | To | Trigger |
|------|-----|---------|
| `Initializing` | `Active` | `PromoteAsync` called |
| `Active` | `Deactivating` | A newer shell is promoted for the same name |
| `Active` | `Draining` | `DrainAsync` called directly (no replacement) |
| `Active` | `Disposed` | `DisposeAsync` called directly (skips drain) |
| `Deactivating` | `Draining` | Internal lifecycle engine starts drain |
| `Draining` | `Drained` | All handlers complete, time out, or drain is forced |
| `Drained` | `Disposed` | `DisposeAsync` called (or automatic post-drain disposal) |
| Any non-`Disposed` | `Disposed` | `DisposeAsync` called directly |

**Rules**:
- Transitions are monotonic; backward moves are no-ops or throw.
- Only `Active` shells are eligible for `PromoteAsync`-based replacement target; calling
  `PromoteAsync` on a non-`Initializing` shell throws.
- `DisposeAsync` on an undrained shell transitions directly to `Disposed` (skipping drain).

---

### IShell *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShell.cs`

| Member | Type | Notes |
|--------|------|-------|
| `Descriptor` | `ShellDescriptor` | Immutable identity + metadata |
| `State` | `ShellLifecycleState` | Current lifecycle state; changes atomically |
| `ServiceProvider` | `IServiceProvider` | Resolvable until `Disposed` |

---

### IDrainHandler *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainHandler.cs`

| Member | Notes |
|--------|-------|
| `Task DrainAsync(IDrainExtensionHandle extensionHandle, CancellationToken cancellationToken)` | Called when shell enters `Draining`. Token is cancelled at deadline. |

**Registration**: Registered as transient in the shell's `IServiceCollection` via
`IShellFeature.ConfigureServices`. Resolved at drain time via `IEnumerable<IDrainHandler>`.

---

### IDrainExtensionHandle *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainExtensionHandle.cs`

| Member | Notes |
|--------|-------|
| `bool TryExtend(TimeSpan requested, out TimeSpan granted)` | Ask the policy for a deadline extension. Returns false if policy rejects. |

---

### IDrainPolicy *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainPolicy.cs`

| Member | Notes |
|--------|-------|
| `TimeSpan? InitialTimeout` | `null` means unbounded |
| `bool IsUnbounded` | True for `UnboundedDrainPolicy` |
| `bool TryExtend(TimeSpan requested, out TimeSpan granted)` | Called by `IDrainExtensionHandle`; fixed/unbounded always return false / true respectively |

**Concrete implementations** (in `CShells/Lifecycle/Policies/`):

| Type | Behaviour |
|------|-----------|
| `FixedTimeoutDrainPolicy(TimeSpan timeout)` | Default; `TryExtend` always returns false |
| `ExtensibleTimeoutDrainPolicy(TimeSpan initial, TimeSpan cap)` | Grants extensions up to cap |
| `UnboundedDrainPolicy` | No deadline; logs a warning on first use; `TryExtend` grants anything |

---

### IDrainOperation *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IDrainOperation.cs`

| Member | Type | Notes |
|--------|------|-------|
| `Status` | `DrainStatus` | Pending / Completed / TimedOut / Forced |
| `Deadline` | `DateTimeOffset?` | Null when policy is unbounded |
| `Task<DrainResult> WaitAsync(CancellationToken)` | Awaitable; resolves when drain finishes |
| `Task ForceAsync(CancellationToken)` | Cancels all handler tokens; transitions shell to Drained promptly |

**DrainStatus** (enum, in `DrainResult.cs`):

| Value | Meaning |
|-------|---------|
| `Pending` | Drain in progress |
| `Completed` | All handlers finished within deadline |
| `TimedOut` | Deadline elapsed; handlers cancelled |
| `Forced` | `ForceAsync` was called |

---

### DrainResult *(new)*

**Location**: `CShells.Abstractions/Lifecycle/DrainResult.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Shell` | `ShellDescriptor` | The drained shell |
| `Status` | `DrainStatus` | Overall outcome |
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

### IShellLifecycleSubscriber *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShellLifecycleSubscriber.cs`

| Member | Notes |
|--------|-------|
| `Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken)` | Called for every state transition on any shell in the registry |

**Isolation**: Subscriber exceptions are caught, logged, and swallowed (Principle VII) so they
cannot block other subscribers or the state transition itself.

---

### IShellRegistry *(new)*

**Location**: `CShells.Abstractions/Lifecycle/IShellRegistry.cs`

| Member | Signature | Notes |
|--------|-----------|-------|
| `CreateAsync` | `Task<IShell> CreateAsync(string name, string version, Action<IServiceCollection> configure, IReadOnlyDictionary<string,string>? metadata, CancellationToken)` | Creates + registers shell in `Initializing` state. Throws if `ShellId` already exists (FR-019). Exception propagates; no partial entry retained (FR-020). |
| `GetActive` | `IShell? GetActive(string name)` | Returns the single `Active` shell for `name`; null if none (FR-011) |
| `GetAll` | `IReadOnlyCollection<IShell> GetAll(string name)` | All shells for `name` regardless of state (FR-012) |
| `PromoteAsync` | `Task PromoteAsync(IShell shell, CancellationToken)` | Transitions `shell` from `Initializing` → `Active`; transitions previous active to `Deactivating`. Serialized per name (FR-013). Only valid for `Initializing` shells. |
| `DrainAsync` | `Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken)` | Initiates drain on `shell`. Concurrent calls return the same operation (FR-018). |
| `ReplaceAsync` | `Task<IDrainOperation> ReplaceAsync(IShell newShell, CancellationToken)` | Promotes `newShell` and drains the previous active in one atomic call (FR-014). Returns the drain operation for the old shell. |
| `Subscribe` | `void Subscribe(IShellLifecycleSubscriber subscriber)` | Thread-safe subscriber registration |
| `Unsubscribe` | `void Unsubscribe(IShellLifecycleSubscriber subscriber)` | Thread-safe subscriber removal |

---

## Validation Rules

| Rule | Enforcement |
|------|-------------|
| `ShellId` must be unique in registry | `CreateAsync` throws `InvalidOperationException` (FR-019) |
| `PromoteAsync` target must be in `Initializing` state | Throws `InvalidOperationException` if not |
| `DrainAsync` / `ReplaceAsync` target must be `Active` or `Deactivating` | Throws `InvalidOperationException` if already `Draining`, `Drained`, or `Disposed` |
| State transitions are monotonic | CAS on `_state`; backward attempts are no-ops |
| Handler exceptions do not abort drain | Caught; stored in `DrainHandlerResult.Error` |
| Subscriber exceptions do not abort state transitions | Caught, logged, swallowed |

---

## State Transition Diagram

```
                    PromoteAsync
  [Initializing] ─────────────────► [Active]
                                       │
                              ┌────────┴────────┐
                    replace / │                 │ direct
                    promote   │                 │ DrainAsync
                    other     ▼                 │
                        [Deactivating]          │
                              │                 │
                              ▼                 ▼
                           [Draining] ◄─────────┘
                              │
                     handlers complete
                     / timeout / force
                              │
                              ▼
                           [Drained]
                              │
                              ▼
                          [Disposed] ◄── DisposeAsync (from any state)
```
