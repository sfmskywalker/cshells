using System.Collections.Immutable;

namespace CShells.AspNetCore.Routing;

/// <summary>
/// Maps request-side routing identifiers (path segment, host, header value, claim value,
/// root-path opt-in) to a shell blueprint, regardless of activation state. Built from
/// <see cref="CShells.Lifecycle.IShellBlueprintProvider"/> data.
/// </summary>
/// <remarks>
/// <para>
/// The route index is the routing-layer companion to
/// <see cref="CShells.Lifecycle.IShellRegistry"/>. Where the registry holds <em>active</em>
/// generations, the index holds the <em>routing metadata</em> of every blueprint the
/// provider knows about — so a request that arrives before the matching shell has been
/// activated can still be resolved, then handed to
/// <see cref="CShells.Lifecycle.IShellRegistry.GetOrActivateAsync"/> by the middleware.
/// </para>
/// <para>
/// Hot-path lookups for path-by-name routing (the common case) cost at most one
/// <see cref="CShells.Lifecycle.IShellBlueprintProvider.GetAsync"/> call. Lookups for
/// root-path, host, header, and claim modes consult an in-memory snapshot that is populated
/// on first use of those modes and refreshed via lifecycle notifications; the snapshot is
/// never rebuilt eagerly at startup.
/// </para>
/// <para>
/// Implementations MUST be safe to call concurrently. The default implementation publishes
/// its snapshot via volatile reference swap so concurrent reads always observe either the
/// previous fully-consistent snapshot or the new one — never a partial state.
/// </para>
/// </remarks>
public interface IShellRouteIndex
{
    /// <summary>
    /// Resolves a routing match for the supplied criteria, or <c>null</c> when no blueprint
    /// matches.
    /// </summary>
    /// <param name="criteria">
    /// Routing values extracted from the current request by the resolver. Fields not
    /// corresponding to enabled routing modes MUST be left null.
    /// </param>
    /// <param name="cancellationToken">Propagated from the request.</param>
    /// <returns>
    /// A <see cref="ShellRouteMatch"/> identifying the matched blueprint and the routing mode
    /// that produced the match, or <c>null</c> when no match exists.
    /// </returns>
    /// <exception cref="ShellRouteIndexUnavailableException">
    /// The index has never been successfully populated and the underlying provider is
    /// currently failing. Resolvers SHOULD catch this and return <c>null</c> so the request
    /// gets a clean 404 from non-shell middleware rather than a 500.
    /// </exception>
    ValueTask<ShellRouteMatch?> TryMatchAsync(ShellRouteCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a bounded snapshot of the route entries the index currently knows about. Used
    /// by the resolver for the no-match diagnostic log entry.
    /// </summary>
    /// <remarks>
    /// Implementations MUST NOT trigger catalogue enumeration as a side effect of this call;
    /// they return whatever the current snapshot contains (possibly empty).
    /// </remarks>
    /// <param name="maxEntries">
    /// Hard cap on the number of entries returned. Implementations MUST honour the cap;
    /// entries beyond the cap are simply omitted (the caller's log formatter is responsible
    /// for the "(+N more)" suffix).
    /// </param>
    ImmutableArray<ShellRouteEntry> GetCandidateSnapshot(int maxEntries);
}
