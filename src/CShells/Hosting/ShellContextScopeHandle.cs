namespace CShells.Hosting;

/// <summary>
/// Tracks an active request scope against a <see cref="ShellContext"/> so that
/// <see cref="DefaultShellHost"/> can defer disposal of replaced shell contexts until all
/// in-flight request scopes have completed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle:</b> the constructor increments <see cref="ShellContext.ActiveScopes"/> and
/// <see cref="DisposeAsync"/> decrements it. <see cref="DefaultShellHost"/> uses the counter to
/// decide whether it can dispose a replaced shell context immediately or must defer until the
/// last outstanding handle is released.
/// </para>
/// <para>
/// <b>Usage:</b> always consumed via <c>await using</c> in <see cref="ShellMiddleware"/>, which
/// guarantees <see cref="DisposeAsync"/> is called on every exit path (normal, exception, or
/// cancellation). The <c>_released</c> guard below defends against the theoretical case of manual
/// double-dispose, which would otherwise corrupt the counter and prevent the old context from ever
/// being disposed.
/// </para>
/// </remarks>
internal sealed class ShellContextScopeHandle : IAsyncDisposable
{
    private readonly ShellContext _context;
    private readonly DefaultShellHost _host;
    private int _released; // 0 = active, 1 = released; guards against double-dispose

    internal ShellContextScopeHandle(ShellContext context, DefaultShellHost host)
    {
        _context = context;
        _host = host;
        _context.IncrementActiveScopes();
    }

    public async ValueTask DisposeAsync()
    {
        // Guard against double-dispose: each handle must decrement the counter exactly once.
        // With `await using` this never fires in practice, but protects the counter if the handle
        // is ever disposed manually more than once.
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;

        var remaining = _context.DecrementActiveScopes();
        if (remaining == 0 && _context.IsPendingDisposal)
            await _host.TryDisposePendingContextAsync(_context).ConfigureAwait(false);
    }
}
