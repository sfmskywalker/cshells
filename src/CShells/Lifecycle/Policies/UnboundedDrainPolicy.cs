using CShells.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Lifecycle.Policies;

/// <summary>
/// Drain policy with no deadline. Logs a warning on first use; intended for dev/test only.
/// </summary>
public sealed class UnboundedDrainPolicy : IDrainPolicy
{
    private static readonly EventId UnboundedWarning = new(1010, nameof(UnboundedWarning));

    private readonly ILogger<UnboundedDrainPolicy> _logger;
    private int _warned;

    public UnboundedDrainPolicy() : this(NullLogger<UnboundedDrainPolicy>.Instance) { }

    public UnboundedDrainPolicy(ILogger<UnboundedDrainPolicy> logger)
    {
        _logger = Guard.Against.Null(logger);
    }

    /// <inheritdoc />
    public TimeSpan? InitialTimeout => null;

    /// <inheritdoc />
    public bool IsUnbounded => true;

    /// <inheritdoc />
    public bool TryExtend(TimeSpan requested, out TimeSpan granted)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _logger.LogWarning(UnboundedWarning,
                "UnboundedDrainPolicy is in use. Drain operations have no deadline; this is intended for development / test environments only.");
        }

        granted = requested;
        return true;
    }
}
