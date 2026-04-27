namespace CShells.Lifecycle;

/// <summary>
/// Represents a single generation of a named shell with an explicit lifecycle state machine.
/// </summary>
/// <remarks>
/// A shell transitions monotonically through
/// <see cref="ShellLifecycleState.Initializing"/> → <see cref="ShellLifecycleState.Active"/>
/// → <see cref="ShellLifecycleState.Deactivating"/> → <see cref="ShellLifecycleState.Draining"/>
/// → <see cref="ShellLifecycleState.Drained"/> → <see cref="ShellLifecycleState.Disposed"/>.
///
/// <para>
/// <b>Disposal is registry-owned.</b> <see cref="IShell"/> intentionally does NOT implement
/// <see cref="IDisposable"/> or <see cref="IAsyncDisposable"/>; hosts observe disposal via
/// the <see cref="ShellLifecycleState.Drained"/> → <see cref="ShellLifecycleState.Disposed"/>
/// transition event. This preserves the disposal-ordering guarantee in the constitution:
/// a provider is never disposed while services resolved from it are in active use, except
/// under the bounded emergency-shutdown path.
/// </para>
/// </remarks>
public interface IShell
{
    /// <summary>Gets the immutable descriptor identifying this shell generation.</summary>
    ShellDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the current lifecycle state. Reads are consistent without locking; state only
    /// advances forward.
    /// </summary>
    ShellLifecycleState State { get; }

    /// <summary>
    /// Gets the shell's service provider. Resolvable until the shell reaches
    /// <see cref="ShellLifecycleState.Disposed"/>. Drain handlers may resolve services
    /// during the <see cref="ShellLifecycleState.Draining"/> phase.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Creates a tracked DI scope from this shell's provider. Outstanding scopes delay drain's
    /// handler-invocation phase until the scope is disposed or the drain deadline elapses.
    /// </summary>
    /// <returns>A disposable scope handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the shell has already reached <see cref="ShellLifecycleState.Disposed"/>.
    /// Calling <c>BeginScope</c> during <see cref="ShellLifecycleState.Draining"/> is
    /// permitted — the new scope joins the active-scope counter.
    /// </exception>
    IShellScope BeginScope();

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
}
