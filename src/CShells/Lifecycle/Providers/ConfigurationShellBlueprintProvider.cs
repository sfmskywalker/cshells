using System.Collections.Immutable;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using Microsoft.Extensions.Configuration;

namespace CShells.Lifecycle.Providers;

/// <summary>
/// <see cref="IShellBlueprintProvider"/> backed by an <see cref="IConfiguration"/> section
/// whose children are individual shell configurations. Read-only — no
/// <see cref="IShellBlueprintManager"/> is attached.
/// </summary>
/// <remarks>
/// <para>
/// Lookup is O(1) — a direct <c>section.GetSection(name)</c> call. Listing returns entries in
/// case-insensitive ordinal order using the last-returned key as the cursor (research R-008).
/// </para>
/// <para>
/// Each returned blueprint wraps a single child section in a <see cref="ConfigurationShellBlueprint"/>
/// that re-reads the configuration on every <c>ComposeAsync</c> call, so configuration edits
/// between reloads are observed.
/// </para>
/// </remarks>
public sealed class ConfigurationShellBlueprintProvider : IShellBlueprintProvider
{
    private readonly IConfiguration _shellsSection;

    /// <summary>Stable identifier emitted as <see cref="BlueprintSummary.SourceId"/>.</summary>
    public const string SourceIdValue = nameof(ConfigurationShellBlueprintProvider);

    public ConfigurationShellBlueprintProvider(IConfiguration shellsSection)
    {
        _shellsSection = Guard.Against.Null(shellsSection);
    }

    /// <inheritdoc />
    public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        // Match by explicit Name property first, then by key (case-insensitive).
        foreach (var child in _shellsSection.GetChildren())
        {
            var candidate = child["Name"] ?? child.Key;
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                var blueprint = new ConfigurationShellBlueprint(candidate, child);
                return Task.FromResult<ProvidedBlueprint?>(new ProvidedBlueprint(blueprint));
            }
        }

        return Task.FromResult<ProvidedBlueprint?>(null);
    }

    /// <inheritdoc />
    public Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(query);
        query.EnsureValid();

        var ordered = _shellsSection.GetChildren()
            .Select(c => c["Name"] ?? c.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => query.NamePrefix is null ||
                           name.StartsWith(query.NamePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(name => query.Cursor is null ||
                           string.Compare(name, query.Cursor, StringComparison.OrdinalIgnoreCase) > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(query.Limit + 1)
            .ToList();

        var hasMore = ordered.Count > query.Limit;
        var items = ordered.Take(query.Limit)
            .Select(name => new BlueprintSummary(
                name,
                SourceIdValue,
                Mutable: false,
                Metadata: ImmutableDictionary<string, string>.Empty))
            .ToList();

        var nextCursor = hasMore && items.Count > 0 ? items[^1].Name : null;
        return Task.FromResult(new BlueprintPage(items, nextCursor));
    }
}
