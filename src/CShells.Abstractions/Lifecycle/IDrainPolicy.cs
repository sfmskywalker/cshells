namespace CShells.Lifecycle;

/// <summary>
/// Governs drain timeout behaviour: initial deadline, extension decisions, and unbounded mode.
/// </summary>
public interface IDrainPolicy
{
    /// <summary>Gets the initial drain timeout, or <c>null</c> for an unbounded policy.</summary>
    TimeSpan? InitialTimeout { get; }

    /// <summary>
    /// Gets whether this policy places no deadline on drain operations. When <c>true</c>, a
    /// warning is logged before each drain starts. Intended for dev/test only.
    /// </summary>
    bool IsUnbounded { get; }

    /// <summary>Requests an extension to the current drain deadline.</summary>
    /// <param name="requested">The duration the handler is requesting.</param>
    /// <param name="granted">The duration the policy actually grants (may be less than requested).</param>
    /// <returns><c>true</c> if any extension was granted; <c>false</c> otherwise.</returns>
    bool TryExtend(TimeSpan requested, out TimeSpan granted);
}
