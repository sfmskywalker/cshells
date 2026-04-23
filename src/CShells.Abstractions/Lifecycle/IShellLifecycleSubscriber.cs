namespace CShells.Lifecycle;

/// <summary>
/// Receives notifications for every shell lifecycle state transition across every shell in
/// the registry.
/// </summary>
/// <remarks>
/// Subscriber exceptions are caught, logged, and swallowed by the registry so one bad
/// subscriber cannot block peers or the state transition itself.
/// </remarks>
public interface IShellLifecycleSubscriber
{
    /// <summary>Invoked when a shell transitions from <paramref name="previous"/> to <paramref name="current"/>.</summary>
    Task OnStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default);
}
