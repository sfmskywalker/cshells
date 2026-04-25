namespace CShells.Lifecycle;

/// <summary>
/// Configuration for <see cref="IShellRegistry.ReloadActiveAsync"/>.
/// </summary>
/// <remarks>
/// The default <see cref="MaxDegreeOfParallelism"/> of <c>8</c> keeps memory pressure bounded
/// during a reload storm — each concurrent reload allocates a fresh
/// <see cref="IServiceProvider"/> root, so unbounded parallelism can blow through the thread
/// pool and allocate thousands of providers simultaneously in large deployments.
/// </remarks>
/// <param name="MaxDegreeOfParallelism">
/// Maximum number of shells reloaded concurrently. Range: <c>[1, 64]</c>.
/// </param>
public sealed record ReloadOptions(int MaxDegreeOfParallelism = 8)
{
    /// <summary>Minimum allowed parallelism.</summary>
    public const int MinParallelism = 1;

    /// <summary>Maximum allowed parallelism.</summary>
    public const int MaxParallelism = 64;

    /// <summary>Validates <see cref="MaxDegreeOfParallelism"/> is within the allowed range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="MaxDegreeOfParallelism"/> is outside <c>[1, 64]</c>.
    /// </exception>
    public void EnsureValid()
    {
        if (MaxDegreeOfParallelism < MinParallelism || MaxDegreeOfParallelism > MaxParallelism)
            throw new ArgumentOutOfRangeException(
                nameof(MaxDegreeOfParallelism),
                MaxDegreeOfParallelism,
                $"MaxDegreeOfParallelism must be between {MinParallelism} and {MaxParallelism}.");
    }
}
