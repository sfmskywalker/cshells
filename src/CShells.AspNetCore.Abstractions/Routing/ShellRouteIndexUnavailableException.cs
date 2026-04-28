namespace CShells.AspNetCore.Routing;

/// <summary>
/// Raised by <see cref="IShellRouteIndex.TryMatchAsync"/> when the route index has never
/// been successfully populated and the underlying provider is currently failing.
/// </summary>
/// <remarks>
/// <para>
/// Resolvers SHOULD catch this and return <c>null</c> so the request gets a clean 404 from
/// non-shell middleware rather than a 500. The built-in <c>WebRoutingShellResolver</c> does
/// exactly that.
/// </para>
/// <para>
/// This exception is raised ONLY when the index is in its initial-population state and the
/// provider exception leaves it with no usable snapshot. Subsequent failures during
/// incremental refresh DO NOT raise this exception — the previous good snapshot remains
/// active.
/// </para>
/// <para>
/// Path-by-name lookups are NOT degraded by this exception, because they consult the
/// provider directly (no snapshot required). The exception only affects root-path, host,
/// header, and claim modes during initial population.
/// </para>
/// </remarks>
public sealed class ShellRouteIndexUnavailableException : Exception
{
    /// <summary>
    /// Constructs the exception with a descriptive message and the underlying provider
    /// failure.
    /// </summary>
    public ShellRouteIndexUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
