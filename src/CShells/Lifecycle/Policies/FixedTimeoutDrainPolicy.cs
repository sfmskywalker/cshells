using CShells.Lifecycle;

namespace CShells.Lifecycle.Policies;

/// <summary>
/// Default <see cref="IDrainPolicy"/>: bounded by a fixed timeout; no extensions granted.
/// </summary>
public sealed class FixedTimeoutDrainPolicy(TimeSpan timeout) : IDrainPolicy
{
    public FixedTimeoutDrainPolicy() : this(TimeSpan.FromSeconds(30)) { }

    /// <inheritdoc />
    public TimeSpan? InitialTimeout { get; } = timeout > TimeSpan.Zero
        ? timeout
        : throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

    /// <inheritdoc />
    public bool IsUnbounded => false;

    /// <inheritdoc />
    public bool TryExtend(TimeSpan requested, out TimeSpan granted)
    {
        granted = TimeSpan.Zero;
        return false;
    }
}
