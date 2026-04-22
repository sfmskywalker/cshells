# Contract: IShell

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IShell` is the runtime handle to a single generation of a named shell. It exposes the
shell's descriptor (name + generation + metadata), current lifecycle state, and service
provider, and lets callers obtain tracked DI scopes via `BeginScope`. Implementations are
terminal once `Disposed`.

## Interface Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Represents a single generation of a named shell with an explicit lifecycle state machine.
/// </summary>
/// <remarks>
/// A shell transitions monotonically through
/// <see cref="ShellLifecycleState.Initializing"/> →
/// <see cref="ShellLifecycleState.Active"/> →
/// <see cref="ShellLifecycleState.Deactivating"/> →
/// <see cref="ShellLifecycleState.Draining"/> →
/// <see cref="ShellLifecycleState.Drained"/> →
/// <see cref="ShellLifecycleState.Disposed"/>.
/// The service provider is available until <see cref="ShellLifecycleState.Disposed"/>.
/// </remarks>
public interface IShell : IAsyncDisposable
{
    /// <summary>
    /// Gets the immutable descriptor identifying this shell generation.
    /// </summary>
    ShellDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the current lifecycle state. Reads are always consistent; state only advances forward.
    /// </summary>
    ShellLifecycleState State { get; }

    /// <summary>
    /// Gets the shell's service provider.
    /// </summary>
    /// <remarks>
    /// Resolvable until the shell reaches <see cref="ShellLifecycleState.Disposed"/>.
    /// Drain handlers may safely resolve services during the
    /// <see cref="ShellLifecycleState.Draining"/> phase.
    /// </remarks>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Creates a tracked DI scope from this shell's provider.
    /// </summary>
    /// <returns>
    /// An <see cref="IShellScope"/> whose <see cref="IShellScope.ServiceProvider"/> serves
    /// scoped services and whose disposal releases both the DI scope and the active-scope
    /// reference this shell holds.
    /// </returns>
    /// <remarks>
    /// Every outstanding <see cref="IShellScope"/> obtained from this shell delays drain's
    /// handler-invocation phase until the scope is disposed or the drain deadline elapses.
    /// This is how the library preserves in-flight request correctness across reloads.
    /// </remarks>
    IShellScope BeginScope();
}
```

## Behaviour Contract

- `State` is read without locking; writes use `Interlocked.CompareExchange` on the backing
  field.
- `DisposeAsync` transitions the shell directly to `Disposed`, regardless of current state
  (including bypassing drain if called during `Draining`).
- Calling `DisposeAsync` on an already-`Disposed` shell is a no-op.
- `ServiceProvider` MUST NOT be accessed after `Disposed`; doing so may throw
  `ObjectDisposedException`.
- `BeginScope` MUST throw `InvalidOperationException` if called after the shell has reached
  `Disposed`. Calling `BeginScope` during `Draining` is permitted — the new scope joins the
  active-scope counter and delays phase-1 completion until released.
