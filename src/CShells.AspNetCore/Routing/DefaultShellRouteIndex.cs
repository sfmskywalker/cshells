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
    ILogger<DefaultShellRouteIndex>? logger = null) : IShellRouteIndex
{
    private readonly IShellBlueprintProvider _provider = Guard.Against.Null(provider);
    private readonly ILogger<DefaultShellRouteIndex> _logger = logger ?? NullLogger<DefaultShellRouteIndex>.Instance;

    // Concurrency model:
    //   _version is monotonically increased by Invalidate(). The currently published
    //   _snapshot carries the version it was built against; readers compare to detect
    //   staleness. _buildLock + _inflightBuild ensure at most one rebuild is in flight
    //   and concurrent stale readers share its result. Invalidate() does NOT clear
    //   _snapshot — it only bumps the version — so a rebuild that fails can fall back
    //   to the previously-good snapshot rather than surfacing
    //   ShellRouteIndexUnavailableException to traffic that was being served fine.
    private long _version;
    private VersionedSnapshot? _snapshot;
    private readonly object _buildLock = new();
    private Task<ShellRouteIndexSnapshot>? _inflightBuild;

    private sealed record VersionedSnapshot(ShellRouteIndexSnapshot Snapshot, long Version);

    /// <inheritdoc />
    public async ValueTask<ShellRouteMatch?> TryMatchAsync(
        ShellRouteCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(criteria);

        // Path-by-name fast path. Hot path for the common multi-tenant deployment.
        var pathByNameAttempted = criteria.PathFirstSegment is { Length: > 0 };
        if (pathByNameAttempted)
        {
            var match = await TryMatchByPathSegmentAsync(criteria.PathFirstSegment!, cancellationToken).ConfigureAwait(false);
            if (match is not null)
                return match;
        }

        // The snapshot is consulted for non-name modes AND for the root-path fallback that
        // catches a path-by-name miss. Without the latter, e.g. "/elsa/api/..." against a
        // deployment whose only blueprint declares WebRouting:Path = "" would 404 — exactly
        // the cold-start regression the route index is meant to eliminate.
        if (!NeedsSnapshot(criteria) && !pathByNameAttempted)
            return null;

        var snapshot = await EnsureSnapshotAsync(cancellationToken).ConfigureAwait(false);

        // Legacy priority: path > host > header > claim > root.
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

        // Root-path: explicit "/" OR fallback after a path-by-name miss.
        if ((criteria.IsRootPath || pathByNameAttempted)
            && snapshot.RootPathEntry is not null && !snapshot.RootPathAmbiguous)
        {
            return new ShellRouteMatch(new ShellId(snapshot.RootPathEntry.ShellName), ShellRoutingMode.RootPath);
        }

        return null;
    }

    /// <inheritdoc />
    public ShellRouteCandidateSnapshot GetCandidateSnapshot(int maxEntries)
    {
        var versioned = Volatile.Read(ref _snapshot);
        if (versioned is null || versioned.Snapshot.All.IsDefaultOrEmpty)
            return new ShellRouteCandidateSnapshot([], 0);

        var entries = versioned.Snapshot.All;
        var total = entries.Length;
        if (maxEntries <= 0)
            return new ShellRouteCandidateSnapshot([], total);

        if (total <= maxEntries)
            return new ShellRouteCandidateSnapshot(entries, total);

        var builder = ImmutableArray.CreateBuilder<ShellRouteEntry>(maxEntries);
        for (var i = 0; i < maxEntries; i++)
            builder.Add(entries[i]);
        return new ShellRouteCandidateSnapshot(builder.ToImmutable(), total);
    }

    /// <summary>
    /// Marks the snapshot stale so the next non-name-mode lookup rebuilds it. Called by
    /// <see cref="ShellRouteIndexInvalidator"/> on relevant lifecycle transitions.
    /// </summary>
    /// <remarks>
    /// Bumps the version counter without clearing the published snapshot. Readers detect
    /// staleness by comparing the published snapshot's version to the current version, so
    /// a rebuild that fails (transient provider outage) can transparently fall back to the
    /// previously-good snapshot. Invalidations that occur during an in-flight rebuild are
    /// not lost: the in-flight rebuild's result is published with its <em>start-of-build</em>
    /// version, which is now older than the current version, so the next reader treats it
    /// as stale and triggers another rebuild.
    /// </remarks>
    internal void Invalidate() => Interlocked.Increment(ref _version);

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
        var versioned = Volatile.Read(ref _snapshot);
        if (versioned is null)
            return false;

        foreach (var entry in versioned.Snapshot.All)
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
        var currentVersion = Interlocked.Read(ref _version);

        // Fresh: the published snapshot was built against the current version. Hot path.
        if (current is not null && current.Version == currentVersion)
            return current.Snapshot;

        // Stale or missing — start (or join) a single shared rebuild. Concurrent stale
        // readers all observe the same in-flight Task and await its result.
        Task<ShellRouteIndexSnapshot> rebuild;
        lock (_buildLock)
        {
            if (_inflightBuild is null || _inflightBuild.IsCompleted)
                _inflightBuild = BuildAndPublishAsync(currentVersion);
            rebuild = _inflightBuild;
        }

        return await rebuild.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ShellRouteIndexSnapshot> BuildAndPublishAsync(long versionAtStart)
    {
        // The build is shared across all stale readers, so it intentionally does NOT honour
        // any single caller's CancellationToken — the caller's WaitAsync(ct) cancels the
        // wait, not the underlying enumeration. Provider-side timeouts bound the work.
        try
        {
            var built = await BuildSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _snapshot, new VersionedSnapshot(built, versionAtStart));
            return built;
        }
        catch (Exception ex)
        {
            // Last-good fallback: a previously-built snapshot is preferable to surfacing
            // ShellRouteIndexUnavailableException for traffic that was being served fine.
            // Stale-but-usable routing is the right behaviour during a transient provider
            // outage; the next invalidation/refresh attempt will retry from scratch.
            var existing = Volatile.Read(ref _snapshot);
            if (existing is not null)
            {
                _logger.LogWarning(ex,
                    "Route-index rebuild failed; continuing to serve previous snapshot (stale).");
                return existing.Snapshot;
            }

            // Initial population failure with no fallback. Surface to the resolver, which
            // logs and falls through to the next strategy.
            _logger.LogWarning(ex,
                "Initial route-index population failed; non-name-mode routing will surface ShellRouteIndexUnavailableException until the next attempt succeeds.");
            throw new ShellRouteIndexUnavailableException(
                $"Route index initial population failed via provider '{_provider.GetType().Name}'.", ex);
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

            // Host mode CAN collide across blueprints (two shells declaring the same
            // WebRouting:Host); the duplicate-detection log is meaningful here.
            if (entry.Host is { Length: > 0 } host && !byHost.TryAdd(host, entry))
                LogDuplicateMode("Host", host, byHost[host].ShellName, entry.ShellName);

            // Header / claim modes are keyed by ShellName (the request's header or claim
            // value is interpreted as the target shell name per the routing convention).
            // ShellNames are unique per provider, so these can't collide — no duplicate-
            // detection log is meaningful. Direct assignment keeps the code honest about
            // that invariant.
            if (entry.HeaderName is { Length: > 0 })
                byHeaderValue[entry.ShellName] = entry;

            if (entry.ClaimKey is { Length: > 0 })
                byClaimValue[entry.ShellName] = entry;
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
