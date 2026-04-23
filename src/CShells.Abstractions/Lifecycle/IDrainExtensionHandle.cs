namespace CShells.Lifecycle;

/// <summary>
/// Allows a drain handler to request a deadline extension from the active
/// <see cref="IDrainPolicy"/>.
/// </summary>
public interface IDrainExtensionHandle
{
    /// <summary>Requests that the drain deadline be extended by <paramref name="requested"/>.</summary>
    /// <param name="requested">The additional time requested.</param>
    /// <param name="granted">The actual extension granted by the policy (may be less than requested).</param>
    /// <returns><c>true</c> if the policy granted at least some extension; <c>false</c> otherwise.</returns>
    bool TryExtend(TimeSpan requested, out TimeSpan granted);
}
