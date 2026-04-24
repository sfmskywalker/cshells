using System.Collections.Immutable;
using System.Text.Json;
using CShells.Configuration;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using FluentStorage.Blobs;
using Microsoft.Extensions.Configuration;

namespace CShells.Providers.FluentStorage;

/// <summary>
/// A <see cref="IShellBlueprint"/> backed by a single JSON blob in <see cref="IBlobStorage"/>.
/// Re-opens, reads, and deserializes the blob on every <see cref="ComposeAsync"/> call so
/// updates between reloads are observed.
/// </summary>
public sealed class FluentStorageShellBlueprint : IShellBlueprint
{
    private readonly IBlobStorage _blobStorage;
    private readonly string _blobFullPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public FluentStorageShellBlueprint(
        string name,
        IBlobStorage blobStorage,
        string blobFullPath,
        JsonSerializerOptions jsonOptions,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Name = Guard.Against.NullOrWhiteSpace(name);
        _blobStorage = Guard.Against.Null(blobStorage);
        _blobFullPath = Guard.Against.NullOrWhiteSpace(blobFullPath);
        _jsonOptions = Guard.Against.Null(jsonOptions);
        Metadata = metadata is null
            ? ImmutableDictionary<string, string>.Empty
            : ImmutableDictionary.CreateRange(metadata);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <inheritdoc />
    public async Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
    {
        var shellConfig = await ReadBlobAsync(cancellationToken).ConfigureAwait(false);

        if (!string.Equals(shellConfig.Name, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Blob '{_blobFullPath}' declares shell '{shellConfig.Name}' but this blueprint is registered under name '{Name}'.");
        }

        // Delegate the shape-handling (features array / object / map, settings flattening) to
        // the existing ConfigurationShellBlueprint by binding into an in-memory IConfiguration
        // snapshot built from this compose cycle's blob content.
        var inMemory = BuildInMemoryConfiguration(shellConfig);
        var inner = new ConfigurationShellBlueprint(Name, inMemory);
        return await inner.ComposeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ShellConfig> ReadBlobAsync(CancellationToken cancellationToken)
    {
        await using var stream = await _blobStorage.OpenReadAsync(_blobFullPath, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<ShellConfig>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Failed to deserialize shell configuration from blob '{_blobFullPath}'.");
    }

    private static IConfiguration BuildInMemoryConfiguration(ShellConfig config)
    {
        var pairs = new Dictionary<string, string?>();
        for (var i = 0; i < config.Features.Count; i++)
        {
            var entry = config.Features[i];
            pairs[$"Features:{i}:Name"] = entry.Name;
            foreach (var kv in entry.Settings)
                pairs[$"Features:{i}:Settings:{kv.Key}"] = kv.Value?.ToString();
        }
        foreach (var kv in config.Configuration)
            pairs[$"Configuration:{kv.Key}"] = kv.Value?.ToString();

        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }
}

/// <summary>
/// FluentStorage-backed blueprint catalogue. Implements both <see cref="IShellBlueprintProvider"/>
/// (read) and <see cref="IShellBlueprintManager"/> (write) since the underlying blob container
/// accepts mutation.
/// </summary>
/// <remarks>
/// <para>
/// Blueprint names map 1:1 to blob filenames — <c>{name}.json</c> under the configured folder.
/// This convention keeps <see cref="GetAsync"/> O(1) (no full-catalogue scan required) and
/// makes <see cref="CreateAsync"/> / <see cref="DeleteAsync"/> a single blob operation.
/// </para>
/// <para>
/// All I/O is async end-to-end — no <c>GetAwaiter().GetResult()</c> anywhere in this provider
/// or its DI registration.
/// </para>
/// </remarks>
public sealed class FluentStorageShellBlueprintProvider : IShellBlueprintProvider, IShellBlueprintManager
{
    private readonly IBlobStorage _blobStorage;
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Stable identifier emitted as <see cref="BlueprintSummary.SourceId"/>.</summary>
    public const string SourceIdValue = nameof(FluentStorageShellBlueprintProvider);

    public FluentStorageShellBlueprintProvider(
        IBlobStorage blobStorage,
        string? path = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        _blobStorage = Guard.Against.Null(blobStorage);
        _path = path ?? string.Empty;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new FeatureEntryListJsonConverter() },
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// This implementation returns <c>true</c> unconditionally — the blob-backed source claims
    /// any syntactically valid name. Callers MUST NOT rely on <c>Owns</c> to disambiguate between
    /// multiple <see cref="FluentStorageShellBlueprintProvider"/> instances pointed at different
    /// containers or paths; use <see cref="ExistsAsync"/> or the owning <see cref="ProvidedBlueprint.Manager"/>
    /// reference attached by <see cref="GetAsync"/> for routing decisions instead.
    /// </remarks>
    public bool Owns(string name)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return true;
    }

    /// <inheritdoc />
    public async Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        var fullPath = ResolveBlobPath(name);
        if (!await _blobStorage.ExistsAsync(fullPath, cancellationToken).ConfigureAwait(false))
            return null;

        var blueprint = new FluentStorageShellBlueprint(name, _blobStorage, fullPath, _jsonOptions);
        return new ProvidedBlueprint(blueprint, Manager: this);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return await _blobStorage.ExistsAsync(ResolveBlobPath(name), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Known limitation: FluentStorage's <see cref="IBlobStorage.ListAsync"/> abstraction does
    /// not uniformly expose a server-side cursor or limit across its adapters, so this
    /// implementation lists the entire folder from the backing store on every call and then
    /// filters + paginates in-memory. Per-call cost is therefore O(N) in catalogue size
    /// regardless of <see cref="BlueprintListQuery.Limit"/>.
    /// </para>
    /// <para>
    /// Cursor and <c>NamePrefix</c> filters are honored for correctness (the returned
    /// <see cref="BlueprintPage"/> respects them) but do not reduce the underlying blob-list
    /// fetch. Deployments at >10k blueprints should prefer a provider with native cursor
    /// support (SQL, DynamoDB, etc.) for listing-heavy admin flows.
    /// </para>
    /// </remarks>
    public async Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(query);
        query.EnsureValid();

        var blobs = await _blobStorage.ListAsync(
            new()
            {
                FolderPath = string.IsNullOrEmpty(_path) ? null : _path,
                Recurse = false,
                FilePrefix = null,
            },
            cancellationToken).ConfigureAwait(false);

        // Deterministic ordering for stable pagination: sort by blob name, case-insensitive.
        // Apply cursor + prefix filters before taking the page.
        var ordered = (blobs ?? [])
            .Where(b => b.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(b => BlobNameToShellName(b.Name))
            .Where(name => query.NamePrefix is null ||
                           name.StartsWith(query.NamePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(name => query.Cursor is null ||
                           string.Compare(name, query.Cursor, StringComparison.OrdinalIgnoreCase) > 0)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(query.Limit + 1)
            .ToList();

        var hasMore = ordered.Count > query.Limit;
        var items = ordered.Take(query.Limit)
            .Select(name => new BlueprintSummary(
                name,
                SourceIdValue,
                Mutable: true,
                Metadata: ImmutableDictionary<string, string>.Empty))
            .ToList();

        var nextCursor = hasMore && items.Count > 0 ? items[^1].Name : null;
        return new BlueprintPage(items, nextCursor);
    }

    /// <inheritdoc />
    public async Task CreateAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(settings);
        var name = Guard.Against.NullOrWhiteSpace(settings.Id.Name);

        var fullPath = ResolveBlobPath(name);
        if (await _blobStorage.ExistsAsync(fullPath, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"A blueprint for '{name}' already exists at '{fullPath}'. Use UpdateAsync to replace it.");
        }

        await WriteBlobAsync(fullPath, settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(settings);
        var name = Guard.Against.NullOrWhiteSpace(settings.Id.Name);

        var fullPath = ResolveBlobPath(name);
        if (!await _blobStorage.ExistsAsync(fullPath, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"No blueprint exists for '{name}' at '{fullPath}'. Use CreateAsync to add one.");
        }

        await WriteBlobAsync(fullPath, settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        var fullPath = ResolveBlobPath(name);
        if (!await _blobStorage.ExistsAsync(fullPath, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"No blueprint exists for '{name}' at '{fullPath}'.");
        }

        await _blobStorage.DeleteAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveBlobPath(string name) =>
        string.IsNullOrEmpty(_path) ? $"{name}.json" : $"{_path.TrimEnd('/')}/{name}.json";

    private static string BlobNameToShellName(string blobName) =>
        blobName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? blobName[..^".json".Length]
            : blobName;

    private async Task WriteBlobAsync(string fullPath, ShellSettings settings, CancellationToken cancellationToken)
    {
        var config = ToShellConfig(settings);
        var payload = JsonSerializer.SerializeToUtf8Bytes(config, _jsonOptions);
        using var stream = new MemoryStream(payload);
        await _blobStorage.WriteAsync(fullPath, stream, append: false, cancellationToken).ConfigureAwait(false);
    }

    private static ShellConfig ToShellConfig(ShellSettings settings)
    {
        var features = settings.EnabledFeatures
            .Select(name => new FeatureEntry { Name = name })
            .ToList();

        var config = new Dictionary<string, object?>();
        foreach (var kv in settings.ConfigurationData)
            config[kv.Key] = kv.Value;

        return new ShellConfig
        {
            Name = settings.Id.Name,
            Features = features,
            Configuration = config,
        };
    }
}
