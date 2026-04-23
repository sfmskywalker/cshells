namespace CShells.Lifecycle;

/// <summary>
/// Represents an in-progress or completed drain operation on a shell.
/// </summary>
/// <remarks>
/// Obtain an instance via <see cref="IShellRegistry.DrainAsync"/> or via
/// <see cref="ReloadResult.Drain"/>. Concurrent callers for the same shell receive the same
/// instance.
/// </remarks>
public interface IDrainOperation
{
    /// <summary>Current drain status.</summary>
    DrainStatus Status { get; }

    /// <summary>UTC deadline by which handlers must complete, or <c>null</c> for an unbounded policy.</summary>
    DateTimeOffset? Deadline { get; }

    /// <summary>Awaits drain completion.</summary>
    /// <param name="cancellationToken">
    /// Cancels the await but does NOT cancel the drain itself; the drain continues in the
    /// background regardless.
    /// </param>
    Task<DrainResult> WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels outstanding handler tokens (and any ongoing scope-wait) and transitions the
    /// shell to <see cref="ShellLifecycleState.Drained"/> after the configured grace period.
    /// </summary>
    Task ForceAsync(CancellationToken cancellationToken = default);
}
