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
/// Accepts the <see cref="Features"/> entry both as a JSON string array
/// (<c>"Features": ["Core", "Posts"]</c>) and as an array of objects
/// (<c>"Features": [ { "Name": "Core" }, { "Name": "Analytics", "Settings": { "Top": 10 } } ]</c>),
/// plus an optional <see cref="Configuration"/> subsection whose keys flow into
/// <see cref="ShellSettings.ConfigurationData"/>.
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
        var settings = new ShellSettings(new ShellId(Name));
        var enabledFeatures = new List<string>();

        var featuresSection = _section.GetSection("Features");
        foreach (var featureSection in featuresSection.GetChildren())
        {
            // Three shapes supported:
            //   Array + string:     "Features": [ "Core" ]           → Value="Core", Key="0"
            //   Array + object:     "Features": [ { "Name": "Core" } ] → Name="Core", Key="0"
            //   Object map:         "Features": { "Core": {} }        → Key="Core"
            var featureName = featureSection.Value
                              ?? featureSection["Name"]
                              ?? (IsNumericKey(featureSection.Key) ? null : featureSection.Key);

            if (string.IsNullOrWhiteSpace(featureName))
                continue;

            enabledFeatures.Add(featureName);

            // Settings in array+object form live under `Settings:*`; in object-map form the
            // feature section's direct children (other than `Name`) ARE the settings.
            var settingsSection = featureSection.GetSection("Settings");
            if (settingsSection.Exists())
            {
                foreach (var kv in Flatten(settingsSection))
                    settings.ConfigurationData[$"{featureName}:{kv.Key}"] = kv.Value;
            }
            else if (!IsNumericKey(featureSection.Key))
            {
                foreach (var kv in Flatten(featureSection))
                {
                    if (string.Equals(kv.Key, "Name", StringComparison.OrdinalIgnoreCase))
                        continue;
                    settings.ConfigurationData[$"{featureName}:{kv.Key}"] = kv.Value;
                }
            }
        }

        settings.EnabledFeatures = [.. enabledFeatures];

        var configurationSection = _section.GetSection("Configuration");
        foreach (var kv in Flatten(configurationSection))
            settings.ConfigurationData[kv.Key] = kv.Value;

        return Task.FromResult(settings);
    }

    private static bool IsNumericKey(string key) => int.TryParse(key, out _);

    /// <summary>Flattens a configuration section to a flat key → value map using colon joins.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Flatten(IConfiguration section)
    {
        foreach (var child in section.GetChildren())
        {
            if (child.Value is not null)
            {
                yield return new KeyValuePair<string, string>(child.Key, child.Value);
                continue;
            }

            foreach (var descendant in Flatten(child))
                yield return new KeyValuePair<string, string>($"{child.Key}:{descendant.Key}", descendant.Value);
        }
    }
}
