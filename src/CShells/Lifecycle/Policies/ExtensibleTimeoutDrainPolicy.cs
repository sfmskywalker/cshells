using CShells.Lifecycle;

namespace CShells.Lifecycle.Policies;

/// <summary>
/// Drain policy that honours extension requests from handlers up to a cumulative cap.
/// </summary>
public sealed class ExtensibleTimeoutDrainPolicy : IDrainPolicy
{
    private readonly TimeSpan _cap;
    private long _cumulativeExtensionTicks;

    public ExtensibleTimeoutDrainPolicy(TimeSpan initial, TimeSpan cap)
    {
        if (initial <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initial), "Initial timeout must be positive.");
        if (cap < initial)
            throw new ArgumentOutOfRangeException(nameof(cap), "Cap must be >= initial timeout.");

        InitialTimeout = initial;
        _cap = cap;
    }

    /// <inheritdoc />
    public TimeSpan? InitialTimeout { get; }

    /// <inheritdoc />
    public bool IsUnbounded => false;

    /// <inheritdoc />
    public bool TryExtend(TimeSpan requested, out TimeSpan granted)
    {
        if (requested <= TimeSpan.Zero)
        {
            granted = TimeSpan.Zero;
            return false;
        }

        // Cumulative extension tracking: the total timeline (initial + all extensions) may not
        // exceed cap. Thread-safe via CAS on a ticks counter.
        while (true)
        {
            var current = Interlocked.Read(ref _cumulativeExtensionTicks);
            var available = _cap - InitialTimeout!.Value - new TimeSpan(current);
            if (available <= TimeSpan.Zero)
            {
                granted = TimeSpan.Zero;
                return false;
            }

            var grant = requested < available ? requested : available;
            var next = current + grant.Ticks;
            if (Interlocked.CompareExchange(ref _cumulativeExtensionTicks, next, current) == current)
            {
                granted = grant;
                return true;
            }
        }
    }
}
