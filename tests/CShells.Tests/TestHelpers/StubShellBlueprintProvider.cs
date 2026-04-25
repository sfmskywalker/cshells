using System.Collections.Concurrent;
using CShells.Lifecycle;

namespace CShells.Tests.TestHelpers;

/// <summary>
/// Controllable <see cref="IShellBlueprintProvider"/> for tests. Tracks lookup and listing
/// counts, supports configurable fault injection, and exposes an ergonomic API to add
/// blueprints paired with optional managers.
/// </summary>
public sealed class StubShellBlueprintProvider : IShellBlueprintProvider
{
    private readonly ConcurrentDictionary<string, ProvidedBlueprint> _blueprints =
        new(StringComparer.OrdinalIgnoreCase);
    private int _lookupCount;
    private int _listCount;

    /// <summary>Stable provider identifier emitted in <see cref="BlueprintSummary.SourceId"/>.</summary>
    public const string SourceIdValue = "Stub";

    /// <summary>How many times <see cref="GetAsync"/> has been invoked (across all names).</summary>
    public int LookupCount => Volatile.Read(ref _lookupCount);

    /// <summary>How many times <see cref="ListAsync"/> has been invoked.</summary>
    public int ListCount => Volatile.Read(ref _listCount);

    /// <summary>When set, <see cref="GetAsync"/> throws this exception instead of returning a blueprint.</summary>
    public Exception? ThrowOnGet { get; set; }

    /// <summary>When set, <see cref="ListAsync"/> throws this exception instead of returning a page.</summary>
    public Exception? ThrowOnList { get; set; }

    public StubShellBlueprintProvider Add(string name, Action<Configuration.ShellBuilder>? configure = null, IShellBlueprintManager? manager = null)
    {
        var blueprint = new Lifecycle.Blueprints.DelegateShellBlueprint(name, configure ?? (_ => { }));
        _blueprints[name] = new ProvidedBlueprint(blueprint, manager);
        return this;
    }

    public StubShellBlueprintProvider Add(IShellBlueprint blueprint, IShellBlueprintManager? manager = null)
    {
        Guard.Against.Null(blueprint);
        _blueprints[blueprint.Name] = new ProvidedBlueprint(blueprint, manager);
        return this;
    }

    public StubShellBlueprintProvider Clear()
    {
        _blueprints.Clear();
        return this;
    }

    /// <inheritdoc />
    public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _lookupCount);
        if (ThrowOnGet is not null)
            throw ThrowOnGet;
        return Task.FromResult(_blueprints.TryGetValue(name, out var provided) ? provided : null);
    }

    /// <inheritdoc />
    public Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _listCount);
        if (ThrowOnList is not null)
            throw ThrowOnList;

        var ordered = _blueprints
            .Where(kv => query.NamePrefix is null ||
                         kv.Key.StartsWith(query.NamePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(kv => query.Cursor is null ||
                         string.Compare(kv.Key, query.Cursor, StringComparison.OrdinalIgnoreCase) > 0)
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(query.Limit + 1)
            .ToList();

        var hasMore = ordered.Count > query.Limit;
        var items = ordered.Take(query.Limit)
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

/// <summary>Minimal test-double <see cref="IShellBlueprintManager"/> that records operations.</summary>
public sealed class StubShellBlueprintManager(string ownedPrefix = "") : IShellBlueprintManager
{
    public ConcurrentQueue<(string Op, string Name)> Operations { get; } = new();
    public Exception? ThrowOnDelete { get; set; }

    public bool Owns(string name) => name.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase);

    public Task CreateAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Operations.Enqueue(("Create", settings.Id.Name));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Operations.Enqueue(("Update", settings.Id.Name));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        Operations.Enqueue(("Delete", name));
        if (ThrowOnDelete is not null)
            throw ThrowOnDelete;
        return Task.CompletedTask;
    }
}
