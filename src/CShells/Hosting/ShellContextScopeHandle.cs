namespace CShells.Hosting;

/// <summary>
/// Tracks an active request scope against a <see cref="ShellContext"/> so that
/// <see cref="DefaultShellHost"/> can defer disposal of replaced shell contexts until all
/// in-flight request scopes have completed.
/// </summary>
internal sealed class ShellContextScopeHandle : IAsyncDisposable
{
    private readonly ShellContext _context;
    private readonly DefaultShellHost _host;

    internal ShellContextScopeHandle(ShellContext context, DefaultShellHost host)
    {
        _context = context;
        _host = host;
        _context.IncrementActiveScopes();
    }

    public async ValueTask DisposeAsync()
    {
        var remaining = _context.DecrementActiveScopes();
        if (remaining == 0 && _context.IsPendingDisposal)
            await _host.TryDisposePendingContextAsync(_context).ConfigureAwait(false);
    }
}
