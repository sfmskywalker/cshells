using System.Text.Json;
using CShells.Configuration;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using FluentStorage.Blobs;
using Microsoft.Extensions.Configuration;

namespace CShells.Providers.FluentStorage;

/// <summary>
/// Loads shell blueprints from blob storage using FluentStorage. Each <c>*.json</c> blob at
/// the configured path represents one shell's configuration; the loader returns a
/// <see cref="ConfigurationShellBlueprint"/> per blob that re-reads the blob on every
/// activation / reload.
/// </summary>
public sealed class FluentStorageShellBlueprintLoader
{
    private readonly IBlobStorage _blobStorage;
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;

    public FluentStorageShellBlueprintLoader(
        IBlobStorage blobStorage,
        string? path = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        _blobStorage = Guard.Against.Null(blobStorage);
        _path = path ?? string.Empty;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new FeatureEntryListJsonConverter() }
        };
    }

    /// <summary>
    /// Enumerates a blueprint per shell-config blob. Each blueprint wraps an
    /// <see cref="IConfiguration"/> built from the blob's current JSON, so subsequent reloads
    /// pick up blob updates.
    /// </summary>
    public async Task<IReadOnlyList<IShellBlueprint>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var blobs = await _blobStorage.ListAsync(
            new()
            {
                FolderPath = string.IsNullOrEmpty(_path) ? null : _path,
                Recurse = false,
                FilePrefix = null
            },
            cancellationToken);

        if (blobs is null || !blobs.Any())
            return [];

        var blueprints = new List<IShellBlueprint>();

        foreach (var blob in blobs.Where(b => b.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            await using var stream = await _blobStorage.OpenReadAsync(blob.FullPath, cancellationToken);
            var shellConfig = await JsonSerializer.DeserializeAsync<ShellConfig>(stream, _jsonOptions, cancellationToken)
                ?? throw new InvalidOperationException($"Failed to deserialize shell configuration from '{blob.Name}'.");

            if (string.IsNullOrWhiteSpace(shellConfig.Name))
                throw new InvalidOperationException($"Shell config in blob '{blob.Name}' is missing its Name property.");

            // Build an in-memory IConfiguration from the deserialized ShellConfig so the
            // blueprint's ComposeAsync re-binds into fresh ShellSettings on every call.
            var memory = BuildMemoryConfiguration(shellConfig);
            blueprints.Add(new ConfigurationShellBlueprint(shellConfig.Name, memory));
        }

        return blueprints;
    }

    private static IConfiguration BuildMemoryConfiguration(ShellConfig config)
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
