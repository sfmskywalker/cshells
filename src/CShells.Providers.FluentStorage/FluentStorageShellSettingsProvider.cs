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
/// Enumerates JSON blobs at the configured path and contributes one
/// <see cref="FluentStorageShellBlueprint"/> per blob. Runs asynchronously during host start
/// via the <see cref="IShellBlueprintProvider"/> seam — no sync-over-async at DI time.
/// </summary>
public sealed class FluentStorageShellBlueprintProvider : IShellBlueprintProvider
{
    private readonly IBlobStorage _blobStorage;
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;

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
    public async Task<IReadOnlyList<IShellBlueprint>> GetBlueprintsAsync(CancellationToken cancellationToken = default)
    {
        var blobs = await _blobStorage.ListAsync(
            new()
            {
                FolderPath = string.IsNullOrEmpty(_path) ? null : _path,
                Recurse = false,
                FilePrefix = null,
            },
            cancellationToken).ConfigureAwait(false);

        if (blobs is null || !blobs.Any())
            return [];

        var blueprints = new List<IShellBlueprint>();

        foreach (var blob in blobs.Where(b => b.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            // Peek the blob once to establish the shell's canonical name. The deserialized
            // content is NOT cached — ComposeAsync re-opens the blob on every activation /
            // reload so updates are observed without a restart.
            await using var stream = await _blobStorage.OpenReadAsync(blob.FullPath, cancellationToken).ConfigureAwait(false);
            var shellConfig = await JsonSerializer.DeserializeAsync<ShellConfig>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Failed to deserialize shell configuration from blob '{blob.FullPath}'.");

            if (string.IsNullOrWhiteSpace(shellConfig.Name))
                throw new InvalidOperationException($"Shell config in blob '{blob.FullPath}' is missing its Name property.");

            blueprints.Add(new FluentStorageShellBlueprint(
                shellConfig.Name,
                _blobStorage,
                blob.FullPath,
                _jsonOptions));
        }

        return blueprints;
    }
}
