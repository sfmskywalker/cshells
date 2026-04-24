using System.Collections.Concurrent;
using CShells.Lifecycle;

namespace CShells.Lifecycle.Providers;

/// <summary>
/// In-memory <see cref="IShellBlueprintProvider"/>. Backs the fluent <c>AddShell(...)</c>
/// builder API and any host code that wants to vend code-seeded blueprints.
/// </summary>
/// <remarks>
/// <para>
/// Storage is a thread-safe, case-insensitive <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// <see cref="GetAsync"/> is O(1). <see cref="ListAsync"/> emits entries sorted by name in
/// case-insensitive ordinal order; the cursor is the last-returned name (research R-008).
/// </para>
/// <para>
/// Per-name optional <see cref="IShellBlueprintManager"/> association is supported via
/// <see cref="Add(IShellBlueprint, IShellBlueprintManager)"/> — typically unused for the pure
/// code-seeded case, but kept available for tests and for providers composed in-process.
/// </para>
/// </remarks>
public sealed class InMemoryShellBlueprintProvider : IShellBlueprintProvider
{
    private readonly ConcurrentDictionary<string, ProvidedBlueprint> _blueprints =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stable identifier emitted as <see cref="BlueprintSummary.SourceId"/>.</summary>
    public const string SourceIdValue = nameof(InMemoryShellBlueprintProvider);

    public InMemoryShellBlueprintProvider() { }

    public InMemoryShellBlueprintProvider(IEnumerable<IShellBlueprint> blueprints)
    {
        Guard.Against.Null(blueprints);
        foreach (var blueprint in blueprints)
            Add(blueprint);
    }

    /// <summary>Adds a read-only blueprint. Throws if the name is already present.</summary>
    public void Add(IShellBlueprint blueprint)
    {
        Guard.Against.Null(blueprint);
        Guard.Against.NullOrWhiteSpace(blueprint.Name, nameof(blueprint) + "." + nameof(blueprint.Name));

        if (!_blueprints.TryAdd(blueprint.Name, new ProvidedBlueprint(blueprint)))
            throw new InvalidOperationException(
                $"A blueprint for '{blueprint.Name}' is already registered with the in-memory provider.");
    }

    /// <summary>Adds a blueprint paired with a manager (i.e., the source is mutable).</summary>
    public void Add(IShellBlueprint blueprint, IShellBlueprintManager manager)
    {
        Guard.Against.Null(blueprint);
        Guard.Against.Null(manager);
        Guard.Against.NullOrWhiteSpace(blueprint.Name, nameof(blueprint) + "." + nameof(blueprint.Name));

        if (!_blueprints.TryAdd(blueprint.Name, new ProvidedBlueprint(blueprint, manager)))
            throw new InvalidOperationException(
                $"A blueprint for '{blueprint.Name}' is already registered with the in-memory provider.");
    }

    /// <inheritdoc />
    public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return Task.FromResult(_blueprints.TryGetValue(name, out var provided) ? provided : null);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return Task.FromResult(_blueprints.ContainsKey(name));
    }

    /// <inheritdoc />
    public Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(query);
        query.EnsureValid();

        // Snapshot + sort. Catalogue mutations during iteration may skip or duplicate per
        // FR-acceptance; the in-memory case handles this cleanly because we sort the snapshot.
        var sorted = _blueprints
            .Where(kv => query.NamePrefix is null ||
                        kv.Key.StartsWith(query.NamePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(kv => query.Cursor is null ||
                        string.Compare(kv.Key, query.Cursor, StringComparison.OrdinalIgnoreCase) > 0)
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(query.Limit + 1)  // +1 to detect whether there's a next page
            .ToList();

        var hasMore = sorted.Count > query.Limit;
        var items = sorted.Take(query.Limit)
            .Select(kv => new BlueprintSummary(
                kv.Key,
                SourceIdValue,
                Mutable: kv.Value.Manager is not null,
                kv.Value.Blueprint.Metadata))
            .ToList();

        var nextCursor = hasMore && items.Count > 0 ? items[^1].Name : null;
        return Task.FromResult(new BlueprintPage(items, nextCursor));
    }
}
