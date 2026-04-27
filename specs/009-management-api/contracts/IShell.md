# Contract: `IShell` (delta — new `Drain` property)

**Feature**: [009-management-api](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/IShell.cs`

`IShell`'s existing public surface (`Descriptor`, `State`, `ServiceProvider`,
`BeginScope`) is unchanged. This document records one addition: the
`Drain` property exposing the in-flight `IDrainOperation` for non-active
generations.

## New member

```csharp
/// <summary>
/// Gets the in-flight drain operation associated with this generation, or <c>null</c>
/// when no drain is in flight.
/// </summary>
/// <remarks>
/// <para>
/// The value is non-null exactly when <see cref="State"/> is one of
/// <see cref="ShellLifecycleState.Deactivating"/>, <see cref="ShellLifecycleState.Draining"/>,
/// or <see cref="ShellLifecycleState.Drained"/>; null when the state is
/// <see cref="ShellLifecycleState.Initializing"/>, <see cref="ShellLifecycleState.Active"/>,
/// or <see cref="ShellLifecycleState.Disposed"/>.
/// </para>
/// <para>
/// The reference returned is the same instance any concurrent caller of
/// <see cref="IShellRegistry.DrainAsync"/> would receive for this shell — exposing it
/// directly makes per-generation drain observability possible without round-tripping
/// through the registry.
/// </para>
/// </remarks>
IDrainOperation? Drain { get; }
```

## Invariants

| State | `Drain` value |
|---|---|
| `Initializing` | `null` |
| `Active` | `null` |
| `Deactivating` | non-null — the in-flight `DrainOperation` |
| `Draining` | non-null — same instance |
| `Drained` | non-null — same instance, terminal status |
| `Disposed` | `null` (cleared by `Shell.DisposeAsync` to break the reference cycle) |

The implementation guarantees publish-once semantics: the first call to
`ShellRegistry.DrainAsync` for a given `Shell` instance CAS-publishes a
single `DrainOperation`; subsequent concurrent callers all observe the
same reference. This preserves the existing `IDrainOperation` contract
("concurrent callers for the same shell receive the same instance").

## Behavioral notes

- Reading `IShell.Drain` is a `Volatile.Read`; safe to call from any
  thread. The result reflects the moment of the read — a state transition
  may have advanced concurrently, in which case the next read may see a
  different value.
- `IShell.Drain.Status` is the canonical signal for "is this drain still
  in flight?" — read it inline rather than re-reading `IShell.State` and
  inferring.
- Calling `IShell.Drain.ForceAsync(...)` on a generation that is already
  in `Drained` state is a no-op; the method returns immediately and a
  follow-up `WaitAsync()` returns the existing terminal `DrainResult`.

## Concurrency contract additions

`IShell.Drain`'s publish is sequenced before the `Active → Deactivating`
state transition: callers that observe `State` advance to `Deactivating`
are guaranteed to see a non-null `Drain` on the next read. Implementation
must order the `Shell.PublishDrain(...)` call before the
`ForceAdvanceAsync(Draining)` call inside `ShellRegistry.DrainAsync` (the
existing code already advances state inside `StartDrain`; the new code
publishes first, then advances).

## Removed dependencies

None at the abstraction level. The `ConcurrentDictionary<IShell,
Lazy<DrainOperation>>` removal is an implementation detail of
`ShellRegistry`; consumers never observed it.

## Migration notes

None for external callers — `IShell.Drain` is purely additive. In-process
code that previously round-tripped through `IShellRegistry.DrainAsync(shell)`
to retrieve an in-flight drain reference can now read `shell.Drain`
directly. The old path continues to work unchanged.
