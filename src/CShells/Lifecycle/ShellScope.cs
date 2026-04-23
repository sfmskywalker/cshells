using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Lifecycle;

/// <summary>
/// Tracked DI scope built from a <see cref="Shell"/>'s service provider. Incrementing /
/// decrementing the owning shell's active-scope counter is the scope handle's responsibility;
/// the shell itself observes counter-drop signals to unblock drain's phase 1.
/// </summary>
internal sealed class ShellScope(Shell shell, AsyncServiceScope inner) : IShellScope
{
    private readonly Shell _shell = Guard.Against.Null(shell);
    private readonly AsyncServiceScope _inner = inner;
    private int _disposed;

    /// <inheritdoc />
    public IShell Shell => _shell;

    /// <inheritdoc />
    public IServiceProvider ServiceProvider => _inner.ServiceProvider;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Guard against double-dispose so the counter decrements exactly once.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _shell.DecrementScopeCounter();
        }
    }
}
