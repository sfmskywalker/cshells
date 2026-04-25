namespace CShells.Lifecycle;

/// <summary>
/// Source-agnostic blueprint catalogue. Supports on-demand lookup and paginated listing; does
/// NOT require eager enumeration of the catalogue at any point in normal operation (startup,
/// activation, routing).
/// </summary>
/// <remarks>
/// <para>
/// Exactly one implementation is registered per host. Code-defined shells use the built-in
/// <c>InMemoryShellBlueprintProvider</c> (populated via <c>CShellsBuilder.AddShell</c>);
/// external sources register their own implementation via
/// <c>CShellsBuilder.AddBlueprintProvider</c>. Mixing the two is rejected at composition time.
/// First- and third-party implementations are equally welcome — this is the framework's open
/// extension seam for blueprint sourcing.
/// </para>
/// <para>
/// A provider wrapping a mutable source (e.g., a blob container or a SQL table) pairs the
/// blueprints it vends with an <see cref="IShellBlueprintManager"/> via
/// <see cref="ProvidedBlueprint.Manager"/>. Providers wrapping read-only sources simply omit
/// the manager.
/// </para>
/// <para>
/// Implementations MUST be safe to call concurrently. <see cref="GetAsync"/>,
/// <see cref="ExistsAsync"/>, and <see cref="ListAsync"/> MUST NOT cache per-call state in
/// instance fields.
/// </para>
/// </remarks>
public interface IShellBlueprintProvider
{
    /// <summary>
    /// Returns the blueprint for <paramref name="name"/>, paired with its owning manager if
    /// the underlying source supports mutation. Returns <c>null</c> when the provider does
    /// not claim this name.
    /// </summary>
    /// <remarks>
    /// MUST be O(1) or O(log N) with respect to catalogue size. MUST NOT enumerate the full
    /// catalogue. I/O faults (e.g., database unreachable) throw rather than return <c>null</c>;
    /// the registry wraps the thrown exception in
    /// <see cref="ShellBlueprintUnavailableException"/> before surfacing to callers.
    /// </remarks>
    Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cheap existence check. Default: <c>(await GetAsync(name)) is not null</c>.
    /// Implementations MAY override for a faster path (e.g., a blob-presence check that does
    /// not download the blob content).
    /// </summary>
    async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default) =>
        await GetAsync(name, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>
    /// Paginated listing of blueprints this provider contributes. Given an unchanged
    /// catalogue, calling <c>ListAsync(new BlueprintListQuery())</c> repeatedly MUST yield
    /// the same first page; the overall enumeration order MUST be deterministic so paging is
    /// resumable.
    /// </summary>
    Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default);
}
