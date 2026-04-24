using CShells.Lifecycle;

namespace CShells.Lifecycle.Providers;

/// <summary>
/// Multiplexes an ordered list of <see cref="IShellBlueprintProvider"/> instances as a single
/// logical catalogue. Lookup precedence is DI-registration order: the first sub-provider
/// returning a non-null <see cref="ProvidedBlueprint"/> wins.
/// </summary>
/// <remarks>
/// <para>
/// Duplicate-name detection is configurable via <see cref="CompositeProviderOptions.DetectDuplicatesOnLookup"/>.
/// When enabled, every <see cref="GetAsync"/> probes all sub-providers and raises
/// <see cref="DuplicateBlueprintException"/> if two or more claim the same name. When disabled
/// (default for release builds), <see cref="GetAsync"/> short-circuits at the first hit.
/// Listing always detects duplicates intra- and inter-page because pagination is an admin
/// flow and the cost is negligible compared to the I/O.
/// </para>
/// <para>
/// Pagination uses an opaque base64-JSON composite cursor produced by
/// <see cref="CompositeCursorCodec"/> that multiplexes each sub-provider's own cursor.
/// </para>
/// </remarks>
public sealed class CompositeShellBlueprintProvider : IShellBlueprintProvider
{
    private readonly IReadOnlyList<IShellBlueprintProvider> _providers;
    private readonly CompositeProviderOptions _options;

    public CompositeShellBlueprintProvider(IReadOnlyList<IShellBlueprintProvider> providers)
        : this(providers, CompositeProviderOptions.Default) { }

    public CompositeShellBlueprintProvider(
        IReadOnlyList<IShellBlueprintProvider> providers,
        CompositeProviderOptions options)
    {
        _providers = Guard.Against.Null(providers);
        _options = Guard.Against.Null(options);
    }

    /// <summary>The ordered sub-providers. Exposed for diagnostics and testing.</summary>
    public IReadOnlyList<IShellBlueprintProvider> Providers => _providers;

    /// <inheritdoc />
    public async Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        ProvidedBlueprint? firstHit = null;
        Type? firstProviderType = null;

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var hit = await provider.GetAsync(name, cancellationToken).ConfigureAwait(false);
            if (hit is null)
                continue;

            if (firstHit is null)
            {
                firstHit = hit;
                firstProviderType = provider.GetType();

                if (!_options.DetectDuplicatesOnLookup)
                    return firstHit;

                // Fall through and keep probing to detect duplicates.
                continue;
            }

            throw new DuplicateBlueprintException(name, firstProviderType!, provider.GetType());
        }

        return firstHit;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        foreach (var provider in _providers)
        {
            if (await provider.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(query);
        query.EnsureValid();

        if (_providers.Count == 0)
            return new BlueprintPage([], null);

        // Decode prior cursor into per-sub-provider state. First page = empty state; start at
        // provider index 0 with a null sub-cursor.
        var cursors = CompositeCursorCodec.Decode(query.Cursor);
        var startIndex = cursors.Count > 0 ? cursors[0].ProviderIndex : 0;

        var collected = new List<BlueprintSummary>(query.Limit);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = query.Limit;

        // Track where we stopped so we can encode the continuation cursor correctly.
        // resumeAt = first provider index with unfinished work (either mid-cursor or fully
        // unstarted). -1 means every provider has been exhausted.
        var resumeAt = -1;
        string? resumeSubCursor = null;

        var i = startIndex;
        while (i < _providers.Count && remaining > 0)
        {
            var subCursor = cursors.FirstOrDefault(c => c.ProviderIndex == i)?.SubCursor;
            var subQuery = new BlueprintListQuery(subCursor, remaining, query.NamePrefix);
            var page = await _providers[i].ListAsync(subQuery, cancellationToken).ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                if (!seenNames.Add(item.Name))
                {
                    var prior = collected.FirstOrDefault(s =>
                        string.Equals(s.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                    var priorProvider = prior is null
                        ? _providers[i].GetType()
                        : FindProviderByName(prior.SourceId);
                    throw new DuplicateBlueprintException(item.Name, priorProvider, _providers[i].GetType());
                }
                collected.Add(item);
            }

            remaining -= page.Items.Count;

            if (page.NextCursor is not null)
            {
                // This sub-provider has more pages remaining; resume from here.
                resumeAt = i;
                resumeSubCursor = page.NextCursor;
                break;
            }

            // Sub-provider exhausted. Advance — but only set resumeAt if remaining == 0 AND
            // there's at least one unstarted provider after this one. Otherwise a later iter
            // of the while-loop will pick them up naturally.
            i++;
        }

        // If remaining hit 0 at a sub-provider boundary (no break, loop exited on remaining),
        // any unprocessed providers at index `i` or beyond must be carried forward.
        if (resumeAt < 0 && remaining == 0 && i < _providers.Count)
            resumeAt = i;

        // Emit cursor iff there is further work.
        string? nextCursor = null;
        if (resumeAt >= 0)
        {
            var outstanding = new List<CompositeCursorEntry>(_providers.Count - resumeAt);
            if (resumeSubCursor is not null)
                outstanding.Add(new CompositeCursorEntry(resumeAt, resumeSubCursor));
            else
                outstanding.Add(new CompositeCursorEntry(resumeAt, string.Empty));

            // Carry any unstarted later providers whose state we might need.
            for (var j = resumeAt + 1; j < _providers.Count; j++)
            {
                if (cursors.FirstOrDefault(c => c.ProviderIndex == j) is { } carry)
                    outstanding.Add(carry);
                else
                    outstanding.Add(new CompositeCursorEntry(j, string.Empty));
            }

            nextCursor = CompositeCursorCodec.Encode(outstanding);
        }

        return new BlueprintPage(collected, nextCursor);
    }

    private Type FindProviderByName(string sourceId)
    {
        // Best-effort: prior summaries carry the owning provider's SourceId (typically the
        // provider type name). Match by that.
        foreach (var provider in _providers)
        {
            if (string.Equals(provider.GetType().Name, sourceId, StringComparison.Ordinal))
                return provider.GetType();
        }
        return typeof(object);
    }
}

/// <summary>Configuration for <see cref="CompositeShellBlueprintProvider"/>.</summary>
public sealed class CompositeProviderOptions
{
    /// <summary>
    /// When <c>true</c>, every <c>GetAsync</c> call probes every sub-provider to detect
    /// conflicting ownership of a name. When <c>false</c>, <c>GetAsync</c> short-circuits at
    /// the first non-null hit (faster but may mask configuration errors). Default: <c>true</c>
    /// in Debug builds, <c>false</c> in Release.
    /// </summary>
    public bool DetectDuplicatesOnLookup { get; init; }
#if DEBUG
        = true;
#else
        = false;
#endif

    public static CompositeProviderOptions Default { get; } = new();
}
