using System.Collections.Frozen;
using System.Collections.Immutable;
using CShells.AspNetCore.Routing;
using CShells.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.AspNetCore.Routing;

/// <summary>
/// Default <see cref="IShellRouteIndex"/>. Path-by-name lookups consult the provider
/// directly via <see cref="IShellBlueprintProvider.GetAsync"/> and verify the blueprint's
/// <c>WebRouting:Path</c>. Root-path / host / header / claim mode lookups consult an
/// in-memory snapshot lazily populated on first use and refreshed via lifecycle events.
/// </summary>
internal sealed class DefaultShellRouteIndex(
    IShellBlueprintProvider provider,
    ILogger<DefaultShellRouteIndex>? logger = null) : IShellRouteIndex, IDisposable
{
    private readonly IShellBlueprintProvider _provider = Guard.Against.Null(provider);
    private readonly ILogger<DefaultShellRouteIndex> _logger = logger ?? NullLogger<DefaultShellRouteIndex>.Instance;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private ShellRouteIndexSnapshot? _snapshot;

    public void Dispose() => _refreshGate.Dispose();

    /// <inheritdoc />
    public async ValueTask<ShellRouteMatch?> TryMatchAsync(
        ShellRouteCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(criteria);

        // Path-by-name fast path. Hot path for the common multi-tenant deployment.
        if (criteria.PathFirstSegment is { Length: > 0 } segment)
        {
            var match = await TryMatchByPathSegmentAsync(segment, cancellationToken).ConfigureAwait(false);
            if (match is not null)
                return match;
        }

        // Non-name modes. All consult the snapshot.
        if (NeedsSnapshot(criteria))
        {
            var snapshot = await EnsureSnapshotAsync(cancellationToken).ConfigureAwait(false);

            if (criteria.IsRootPath && snapshot.RootPathEntry is not null && !snapshot.RootPathAmbiguous)
                return new ShellRouteMatch(new ShellId(snapshot.RootPathEntry.ShellName), ShellRoutingMode.RootPath);

            if (criteria.Host is { Length: > 0 } host && snapshot.ByHost.TryGetValue(host, out var hostEntry))
                return new ShellRouteMatch(new ShellId(hostEntry.ShellName), ShellRoutingMode.Host);

            if (criteria.HeaderValue is { Length: > 0 } headerValue
                && snapshot.ByHeaderValue.TryGetValue(headerValue, out var headerEntry)
                && string.Equals(headerEntry.HeaderName, criteria.HeaderName, StringComparison.OrdinalIgnoreCase))
            {
                return new ShellRouteMatch(new ShellId(headerEntry.ShellName), ShellRoutingMode.Header);
            }

            if (criteria.ClaimValue is { Length: > 0 } claimValue
                && snapshot.ByClaimValue.TryGetValue(claimValue, out var claimEntry)
                && string.Equals(claimEntry.ClaimKey, criteria.ClaimKey, StringComparison.OrdinalIgnoreCase))
            {
                return new ShellRouteMatch(new ShellId(claimEntry.ShellName), ShellRoutingMode.Claim);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public ShellRouteCandidateSnapshot GetCandidateSnapshot(int maxEntries)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null || snapshot.All.IsDefaultOrEmpty)
            return new ShellRouteCandidateSnapshot([], 0);

        var total = snapshot.All.Length;
        if (maxEntries <= 0)
            return new ShellRouteCandidateSnapshot([], total);

        if (total <= maxEntries)
            return new ShellRouteCandidateSnapshot(snapshot.All, total);

        var builder = ImmutableArray.CreateBuilder<ShellRouteEntry>(maxEntries);
        for (var i = 0; i < maxEntries; i++)
            builder.Add(snapshot.All[i]);
        return new ShellRouteCandidateSnapshot(builder.ToImmutable(), total);
    }

    /// <summary>
    /// Invalidates the snapshot so the next non-name-mode lookup rebuilds it. Called by
    /// <see cref="ShellRouteIndexInvalidator"/> on relevant lifecycle transitions.
    /// </summary>
    internal void Invalidate() => Volatile.Write(ref _snapshot, null);

    /// <summary>
    /// True iff the current snapshot already contains a route entry for <paramref name="shellName"/>.
    /// Used by <see cref="ShellRouteIndexInvalidator"/> to skip invalidation for routine
    /// lazy-activation events that don't change the routing graph: if the name is already
    /// in the snapshot, its routing metadata was captured when the snapshot was built and
    /// no rebuild is needed.
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> when no snapshot has been built yet — in that state there's
    /// nothing to invalidate, so the caller can also short-circuit. The check is O(N) over
    /// the snapshot's entry list (<c>All</c>); this is fine because lifecycle fan-out is
    /// not request-rate.
    /// </remarks>
    internal bool ContainsShellName(string shellName)
    {
        Guard.Against.NullOrWhiteSpace(shellName);
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null)
            return false;

        foreach (var entry in snapshot.All)
            if (string.Equals(entry.ShellName, shellName, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private async ValueTask<ShellRouteMatch?> TryMatchByPathSegmentAsync(
        string segment,
        CancellationToken cancellationToken)
    {
        ProvidedBlueprint? provided;
        try
        {
            provided = await _provider.GetAsync(segment, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "Provider lookup for path segment '{Segment}' failed; treating as no match.",
                segment);
            return null;
        }

        if (provided is null)
            return null;

        var entry = await ShellRouteIndexBuilder.BuildEntryAsync(provided.Blueprint, _logger, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        // Convention enforcement: path-by-name match requires the blueprint to actually
        // declare WebRouting:Path equal to the segment (case-insensitive). A blueprint that
        // doesn't declare path routing — or declares a different path — is not eligible for
        // this segment, even if its name happens to match.
        if (entry.Path is not null && string.Equals(entry.Path, segment, StringComparison.OrdinalIgnoreCase))
            return new ShellRouteMatch(new ShellId(entry.ShellName), ShellRoutingMode.Path);

        return null;
    }

    private static bool NeedsSnapshot(ShellRouteCriteria criteria) =>
        criteria.IsRootPath
        || criteria.Host is { Length: > 0 }
        || (criteria.HeaderName is { Length: > 0 } && criteria.HeaderValue is { Length: > 0 })
        || (criteria.ClaimKey is { Length: > 0 } && criteria.ClaimValue is { Length: > 0 });

    private async ValueTask<ShellRouteIndexSnapshot> EnsureSnapshotAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _snapshot);
        if (current is not null)
            return current;

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = Volatile.Read(ref _snapshot);
            if (current is not null)
                return current;

            try
            {
                var built = await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _snapshot, built);
                return built;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Initial population failure: leave _snapshot null so the next call retries
                // BuildSnapshotAsync from scratch. The provider may be transiently unavailable.
                _logger.LogWarning(ex,
                    "Initial route-index population failed; non-name-mode routing will surface ShellRouteIndexUnavailableException until the next attempt succeeds.");
                throw new ShellRouteIndexUnavailableException(
                    $"Route index initial population failed via provider '{_provider.GetType().Name}'.", ex);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<ShellRouteIndexSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        var entries = await EnumerateAllEntriesAsync(cancellationToken).ConfigureAwait(false);

        // Note: path-segment matches are NOT indexed here — TryMatchByPathSegmentAsync hits
        // the provider directly via GetAsync(segment) on the request hot path. The snapshot
        // only carries entries needed by non-name modes (root, host, header, claim).
        var byHost = new Dictionary<string, ShellRouteEntry>(StringComparer.OrdinalIgnoreCase);
        var byHeaderValue = new Dictionary<string, ShellRouteEntry>(StringComparer.OrdinalIgnoreCase);
        var byClaimValue = new Dictionary<string, ShellRouteEntry>(StringComparer.OrdinalIgnoreCase);

        ShellRouteEntry? rootPathEntry = null;
        var rootPathAmbiguous = false;

        foreach (var entry in entries)
        {
            if (entry.Path is { Length: 0 })
            {
                if (rootPathEntry is null)
                    rootPathEntry = entry;
                else
                {
                    rootPathAmbiguous = true;
                    _logger.LogWarning(
                        "Multiple blueprints opt into root-path routing (WebRouting:Path = \"\"). Root-path lookups will return null per existing ambiguous-fallthrough semantics. Conflicting: '{First}' and '{Second}'.",
                        rootPathEntry.ShellName, entry.ShellName);
                }
            }

            if (entry.Host is { Length: > 0 } host && !byHost.TryAdd(host, entry))
                LogDuplicateMode("Host", host, byHost[host].ShellName, entry.ShellName);

            if (entry.HeaderName is { Length: > 0 } && !byHeaderValue.TryAdd(entry.ShellName, entry))
                LogDuplicateMode("HeaderName", entry.ShellName, byHeaderValue[entry.ShellName].ShellName, entry.ShellName);

            if (entry.ClaimKey is { Length: > 0 } && !byClaimValue.TryAdd(entry.ShellName, entry))
                LogDuplicateMode("ClaimKey", entry.ShellName, byClaimValue[entry.ShellName].ShellName, entry.ShellName);
        }

        return new ShellRouteIndexSnapshot
        {
            ByHost = byHost.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            ByHeaderValue = byHeaderValue.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            ByClaimValue = byClaimValue.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            RootPathEntry = rootPathAmbiguous ? null : rootPathEntry,
            RootPathAmbiguous = rootPathAmbiguous,
            All = [..entries],
        };
    }

    private async Task<List<ShellRouteEntry>> EnumerateAllEntriesAsync(CancellationToken cancellationToken)
    {
        var entries = new List<ShellRouteEntry>();
        var query = new BlueprintListQuery(Cursor: null, Limit: 100);

        while (true)
        {
            var page = await _provider.ListAsync(query, cancellationToken).ConfigureAwait(false);

            foreach (var summary in page.Items)
            {
                ProvidedBlueprint? provided;
                try
                {
                    provided = await _provider.GetAsync(summary.Name, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to load blueprint '{Name}' for route index; skipping.", summary.Name);
                    continue;
                }

                if (provided is null)
                    continue;

                var entry = await ShellRouteIndexBuilder.BuildEntryAsync(provided.Blueprint, _logger, cancellationToken).ConfigureAwait(false);
                if (entry is not null)
                    entries.Add(entry);
            }

            if (page.NextCursor is null)
                break;

            query = query with { Cursor = page.NextCursor };
        }

        return entries;
    }

    private void LogDuplicateMode(string mode, string value, string firstShell, string secondShell)
    {
        _logger.LogWarning(
            "Duplicate routing key for mode '{Mode}', value '{Value}': blueprint '{First}' wins, blueprint '{Second}' is excluded from this routing mode.",
            mode, value, firstShell, secondShell);
    }
}
