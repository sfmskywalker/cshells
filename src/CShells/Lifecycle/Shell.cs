using CShells.Lifecycle;

namespace CShells.Lifecycle;

/// <summary>
/// The default <see cref="IShell"/> implementation. Monotonic lifecycle state is maintained
/// via <see cref="Interlocked.CompareExchange(ref int, int, int)"/> on an integer backing
/// field; state reads go through <see cref="Volatile.Read(ref int)"/>.
/// </summary>
/// <remarks>
/// Shell disposal is registry-owned. <see cref="DisposeAsync"/> is <c>internal</c> — hosts
/// observe disposal via the <see cref="ShellLifecycleState.Drained"/> →
/// <see cref="ShellLifecycleState.Disposed"/> transition event raised through the registered
/// <paramref name="onStateChanged"/> callback.
/// </remarks>
internal sealed class Shell(
    ShellDescriptor descriptor,
    IServiceProvider serviceProvider,
    Func<IShell, ShellLifecycleState, ShellLifecycleState, Task> onStateChanged) : IShell
{
    private readonly Func<IShell, ShellLifecycleState, ShellLifecycleState, Task> _onStateChanged = Guard.Against.Null(onStateChanged);
    private int _state = (int)ShellLifecycleState.Initializing;
#pragma warning disable CS0649 // Populated by Phase 5 (US6); left unassigned so the field site is already in place for scope tracking.
    private int _activeScopes;
#pragma warning restore CS0649

    /// <inheritdoc />
    public ShellDescriptor Descriptor { get; } = Guard.Against.Null(descriptor);

    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; } = Guard.Against.Null(serviceProvider);

    /// <inheritdoc />
    public ShellLifecycleState State => (ShellLifecycleState)Volatile.Read(ref _state);

    internal int ActiveScopeCount => Volatile.Read(ref _activeScopes);

    /// <inheritdoc />
    public IShellScope BeginScope() => throw new NotImplementedException("Scope tracking is filled in by Phase 5 (US6).");

    /// <summary>
    /// Attempts to transition from <paramref name="expected"/> to <paramref name="next"/>.
    /// Returns <c>true</c> when the CAS succeeds; the state-change callback is awaited as
    /// part of a successful transition so that subscribers observe transitions in-order.
    /// </summary>
    internal async Task<bool> TryTransitionAsync(ShellLifecycleState expected, ShellLifecycleState next)
    {
        if (next <= expected)
            throw new ArgumentOutOfRangeException(nameof(next), "State transitions must advance forward.");

        var expectedInt = (int)expected;
        if (Interlocked.CompareExchange(ref _state, (int)next, expectedInt) != expectedInt)
            return false;

        await _onStateChanged(this, expected, next).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Force-advances this shell to <paramref name="target"/>, but only if the current state
    /// is strictly less than <paramref name="target"/>. Used by the drain path to move
    /// <c>Draining → Drained → Disposed</c>, and by the registry's emergency-dispose path
    /// on host shutdown-timeout breach.
    /// </summary>
    internal async Task ForceAdvanceAsync(ShellLifecycleState target)
    {
        while (true)
        {
            var prevInt = Volatile.Read(ref _state);
            if (prevInt >= (int)target)
                return;

            if (Interlocked.CompareExchange(ref _state, (int)target, prevInt) == prevInt)
            {
                await _onStateChanged(this, (ShellLifecycleState)prevInt, target).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>
    /// Disposes the shell's service provider and transitions the shell to
    /// <see cref="ShellLifecycleState.Disposed"/>. Registry-only entry point.
    /// </summary>
    internal async ValueTask DisposeAsync()
    {
        await ForceAdvanceAsync(ShellLifecycleState.Disposed).ConfigureAwait(false);

        switch (ServiceProvider)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
