# Contract: IShell

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IShell` is the handle to a named, versioned shell instance. It exposes the shell's descriptor,
current lifecycle state, and service provider. Implementations are terminal once `Disposed`.

## Interface Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Represents a named, versioned shell with an explicit lifecycle state machine.
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
    /// Gets the immutable descriptor that identifies this shell.
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
}
```

## Behaviour Contract

- `State` is read without locking; writes use `Interlocked.CompareExchange` on the backing field.
- `DisposeAsync` transitions the shell directly to `Disposed`, regardless of current state
  (including bypassing drain if called during `Draining`).
- Calling `DisposeAsync` on an already-`Disposed` shell is a no-op.
- `ServiceProvider` MUST NOT be accessed after `Disposed`; doing so may throw
  `ObjectDisposedException`.
