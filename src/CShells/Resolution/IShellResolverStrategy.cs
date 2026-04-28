namespace CShells.Resolution;

/// <summary>
/// Defines a strategy for resolving shell identifiers from a resolution context. Multiple
/// strategies can be registered and will be evaluated in <see cref="ResolverOrderAttribute"/>
/// order by <see cref="IShellResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implementations that perform purely synchronous work return a completed task
/// (<c>Task.FromResult&lt;ShellId?&gt;(...)</c>). The pipeline awaits each strategy in order
/// and short-circuits on the first non-null result.
/// </para>
/// <para>
/// Migrated from sync (<c>Resolve</c>) to async in feature <c>010</c>: the built-in
/// path-routing resolver now consults an asynchronous shell route index that may need to
/// query the blueprint provider on the request hot path.
/// </para>
/// </remarks>
public interface IShellResolverStrategy
{
    /// <summary>
    /// Attempts to resolve a shell identifier from the provided context.
    /// </summary>
    /// <param name="context">The resolution context containing data for shell resolution.</param>
    /// <param name="cancellationToken">Propagated from the request.</param>
    /// <returns>A <see cref="ShellId"/> if resolution is successful; otherwise, <c>null</c>.</returns>
    Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken = default);
}
