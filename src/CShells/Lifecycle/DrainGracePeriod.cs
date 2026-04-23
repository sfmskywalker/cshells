namespace CShells.Lifecycle;

/// <summary>
/// Configurable grace period after drain deadline / force before the shell forcibly transitions
/// to <see cref="ShellLifecycleState.Drained"/>. Default 3 seconds.
/// </summary>
public sealed record DrainGracePeriod(TimeSpan Value)
{
    /// <summary>Default grace period.</summary>
    public static readonly DrainGracePeriod Default = new(TimeSpan.FromSeconds(3));
}
