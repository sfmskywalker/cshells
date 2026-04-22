using System.Collections.Immutable;
using CShells.Configuration;
using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;

namespace CShells.Lifecycle.Blueprints;

/// <summary>
/// Configuration-backed <see cref="IShellBlueprint"/>. Re-reads the bound section on every
/// <see cref="ComposeAsync"/> call so configuration edits between reloads are picked up.
/// </summary>
/// <remarks>
/// Accepts any <see cref="IConfiguration"/> section; typically a subsection of
/// <c>Shells:{name}</c> or a standalone file registered via
/// <c>builder.Configuration.AddJsonFile("Shells/payments.json")</c>.
/// </remarks>
public sealed class ConfigurationShellBlueprint : IShellBlueprint
{
    private readonly IConfiguration _section;

    public ConfigurationShellBlueprint(string name, IConfiguration section, IReadOnlyDictionary<string, string>? metadata = null)
    {
        Name = Guard.Against.NullOrWhiteSpace(name);
        _section = Guard.Against.Null(section);
        Metadata = metadata is null
            ? ImmutableDictionary<string, string>.Empty
            : ImmutableDictionary.CreateRange(metadata);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <inheritdoc />
    public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
    {
        // Bind into ShellConfig to reuse the existing Shells/*.json schema + converter plumbing.
        var config = new ShellConfig { Name = Name };
        _section.Bind(config);

        var settings = new ShellSettings(new ShellId(Name))
        {
            EnabledFeatures = [..config.Features
                .Select(f => f.Name)
                .Where(static id => !string.IsNullOrWhiteSpace(id))],
        };

        foreach (var feature in config.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Name))
                continue;

            foreach (var kv in feature.Settings)
            {
                if (kv.Value is not null)
                    settings.ConfigurationData[$"{feature.Name}:{kv.Key}"] = kv.Value;
            }
        }

        foreach (var kv in config.Configuration)
        {
            if (kv.Value is not null)
                settings.ConfigurationData[kv.Key] = kv.Value;
        }

        return Task.FromResult(settings);
    }
}
