using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace CShells.Configuration;

/// <summary>
/// Shared helper methods for processing configuration data into ShellSettings.
/// </summary>
internal static class ConfigurationHelper
{
    private const string SupportedFeatureValueForms = "true, false, string 'true'/'false', or an object";

    /// <summary>
    /// Converts a value to JsonElement for consistent serialization.
    /// </summary>
    public static object? ConvertToJsonElement(object? value)
    {
        if (value == null)
            return null;

        // Already a JsonElement, return as-is
        if (value is JsonElement)
            return value;

        // For primitives and strings, serialize to JsonElement
        if (value is string || value.GetType().IsPrimitive)
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        // For complex objects, serialize and deserialize to JsonElement
        // Use options that handle complex nested structures
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };

            var json = JsonSerializer.Serialize(value, value.GetType(), options);
            return JsonSerializer.Deserialize<JsonElement>(json, options);
        }
        catch
        {
            // If serialization fails, return the original value
            return value;
        }
    }

    /// <summary>
    /// Flattens a configuration section into key-value pairs suitable for IConfiguration.
    /// </summary>
    public static void FlattenConfigurationSection(IConfigurationSection section, string prefix, IDictionary<string, object> target)
    {
        foreach (var child in section.GetChildren())
        {
            var key = $"{prefix}:{child.Key}";

            if (child.GetChildren().Any())
            {
                // Recursively flatten nested sections
                FlattenConfigurationSection(child, key, target);
            }
            else
            {
                target[key] = child.Value!;
            }
        }
    }

    /// <summary>
    /// Flattens a dictionary of settings into key-value pairs suitable for IConfiguration.
    /// </summary>
    public static void FlattenSettings(Dictionary<string, object?> settings, string prefix, IDictionary<string, object> target)
    {
        foreach (var (key, value) in settings)
        {
            if (value == null)
                continue;

            var fullKey = $"{prefix}:{key}";

            if (value is JsonElement jsonElement)
            {
                FlattenJsonElement(jsonElement, fullKey, target);
            }
            else if (value is Dictionary<string, object?> nested)
            {
                FlattenSettings(nested, fullKey, target);
            }
            else
            {
                target[fullKey] = value;
            }
        }
    }

    /// <summary>
    /// Flattens a JsonElement into key-value pairs suitable for IConfiguration.
    /// </summary>
    public static void FlattenJsonElement(JsonElement element, string prefix, IDictionary<string, object> target)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJsonElement(property.Value, $"{prefix}:{property.Name}", target);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJsonElement(item, $"{prefix}:{index}", target);
                    index++;
                }
                break;

            case JsonValueKind.String:
                var stringValue = element.GetString();
                if (stringValue != null)
                {
                    target[prefix] = stringValue;
                }
                break;

            case JsonValueKind.Number:
                target[prefix] = element.GetRawText();
                break;

            case JsonValueKind.True:
                target[prefix] = "True";
                break;

            case JsonValueKind.False:
                target[prefix] = "False";
                break;

            case JsonValueKind.Null:
                // Skip null values
                break;
        }
    }

    /// <summary>
    /// Serializes an IConfigurationSection to JSON string.
    /// </summary>
    public static string SerializeConfigurationSection(IConfigurationSection section)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var child in section.GetChildren())
        {
            var value = child.GetChildren().Any()
                ? (object?)JsonSerializer.Deserialize<JsonElement>(SerializeConfigurationSection(child))
                : child.Value;

            dict[child.Key] = value;
        }

        return JsonSerializer.Serialize(dict);
    }

    /// <summary>
    /// Loads shell configuration from a configuration section into ConfigurationData.
    /// Complex objects are flattened using colon-separated keys for IConfiguration compatibility.
    /// </summary>
    public static void LoadConfigurationFromSection(IConfigurationSection configSection, IDictionary<string, object> targetConfigurationData)
    {
        foreach (var section in configSection.GetChildren())
        {
            var key = section.Key;

            // Check if this is a complex object or a simple value
            if (section.GetChildren().Any())
            {
                // Complex object - flatten to key-value pairs for IConfiguration
                FlattenConfigurationSection(section, key, targetConfigurationData);
            }
            else
            {
                // Simple value
                if (section.Value != null)
                    targetConfigurationData[key] = section.Value;
            }
        }
    }

    /// <summary>
    /// Extracts feature names from a list of feature entries.
    /// </summary>
    public static string[] ExtractFeatureNames(IEnumerable<FeatureEntry> features)
    {
        return features
            .Where(feature => feature.IsEnabled)
            .Select((feature, index) =>
            {
                EnsureFeatureName(feature.Name, $"Configured feature entry at index {index}");
                return feature.Name.Trim();
            })
            .ToArray();
    }

    /// <summary>
    /// Applies normalized feature entries to shell settings.
    /// </summary>
    public static void ApplyFeatureEntries(IEnumerable<FeatureEntry> features, ShellSettings settings)
    {
        foreach (var feature in features)
        {
            EnsureFeatureName(feature.Name, "Configured feature entry");

            if (!feature.IsEnabled)
            {
                DisableFeature(settings, feature.Name);
                continue;
            }

            if (feature.ResetsSettings && feature.Settings.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Feature '{feature.Name}' cannot combine reset semantics with explicit settings.");
            }

            AddEnabledFeature(settings, feature.Name, feature.ResetsSettings);
            PopulateFeatureSettings([feature], settings.ConfigurationData);
        }
    }

    /// <summary>
    /// Adds an enabled feature declaration to shell settings.
    /// </summary>
    public static void AddEnabledFeature(ShellSettings settings, string featureName, bool resetSettings = false)
    {
        Guard.Against.Null(settings);
        EnsureFeatureName(featureName, "Configured feature entry");

        var normalizedName = featureName.Trim();
        var features = settings.EnabledFeatures.ToList();
        if (!features.Contains(normalizedName, StringComparer.OrdinalIgnoreCase))
            features.Add(normalizedName);
        settings.EnabledFeatures = [..features];

        settings.DisabledFeatures = RemoveName(settings.DisabledFeatures, normalizedName);

        if (resetSettings)
        {
            RemoveFeatureSettings(settings.ConfigurationData, normalizedName);
            settings.FeatureSettingResets = AddName(settings.FeatureSettingResets, normalizedName);
        }
        else
        {
            settings.FeatureSettingResets = RemoveName(settings.FeatureSettingResets, normalizedName);
        }
    }

    /// <summary>
    /// Adds an explicit disabled feature declaration to shell settings.
    /// </summary>
    public static void DisableFeature(ShellSettings settings, string featureName)
    {
        Guard.Against.Null(settings);
        EnsureFeatureName(featureName, "Configured feature entry");

        var normalizedName = featureName.Trim();
        settings.EnabledFeatures = RemoveName(settings.EnabledFeatures, normalizedName);
        settings.DisabledFeatures = AddName(settings.DisabledFeatures, normalizedName);
        settings.FeatureSettingResets = RemoveName(settings.FeatureSettingResets, normalizedName);
        RemoveFeatureSettings(settings.ConfigurationData, normalizedName);
    }

    /// <summary>
    /// Removes feature-prefixed configuration keys from a shell configuration dictionary.
    /// </summary>
    public static void RemoveFeatureSettings(IDictionary<string, object> configurationData, string featureName)
    {
        Guard.Against.Null(configurationData);
        EnsureFeatureName(featureName, "Configured feature entry");

        var prefix = $"{featureName.Trim()}:";
        var keys = configurationData.Keys
            .Where(key => key.Equals(featureName, StringComparison.OrdinalIgnoreCase) ||
                          key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in keys)
            configurationData.Remove(key);
    }

    /// <summary>
    /// Describes the structural shape of a <c>Features</c> configuration section.
    /// </summary>
    public enum FeaturesShape { Empty, Array, Object, Ambiguous }

    /// <summary>
    /// Detects the shape of a Features configuration section.
    /// </summary>
    public static FeaturesShape DetectFeaturesShape(IConfigurationSection featuresSection)
    {
        var children = featuresSection.GetChildren().ToList();

        if (children.Count == 0)
            return FeaturesShape.Empty;

        var hasNumeric = false;
        var hasNamed = false;

        foreach (var child in children)
        {
            if (int.TryParse(child.Key, out _))
                hasNumeric = true;
            else
                hasNamed = true;
        }

        if (hasNumeric && hasNamed)
            return FeaturesShape.Ambiguous;

        return hasNumeric ? FeaturesShape.Array : FeaturesShape.Object;
    }

    /// <summary>
    /// Validates that a feature list contains no duplicate configured feature names.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when duplicate feature names are detected.</exception>
    public static void ValidateNoDuplicateFeatures(List<FeatureEntry> entries, string shellName)
    {
        var duplicates = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Shell '{shellName}' contains duplicate configured feature name(s): {string.Join(", ", duplicates.Select(d => $"'{d}'"))}.");
        }
    }

    /// <summary>
    /// Populates configuration data from feature entries.
    /// Settings from each feature are namespaced under the feature name.
    /// </summary>
    public static void PopulateFeatureSettings(IEnumerable<FeatureEntry> features, IDictionary<string, object> configurationData)
    {
        foreach (var feature in features)
        {
            if (!feature.IsEnabled)
                continue;

            if (feature.Settings.Count == 0)
                continue;

            FlattenSettings(feature.Settings, feature.Name, configurationData);
        }
    }

    /// <summary>
    /// Populates shell configuration from a dictionary into ConfigurationData.
    /// Complex objects are flattened using colon-separated keys for IConfiguration compatibility.
    /// </summary>
    public static void PopulateShellConfiguration(Dictionary<string, object?> configuration, IDictionary<string, object> configurationData)
    {
        foreach (var (key, value) in configuration)
        {
            if (value == null)
                continue;

            if (value is JsonElement jsonElement)
            {
                FlattenJsonElement(jsonElement, key, configurationData);
            }
            else if (value is Dictionary<string, object?> nested)
            {
                PopulateShellConfiguration(nested, configurationData);
            }
            else
            {
                configurationData[key] = value;
            }
        }
    }

    /// <summary>
    /// Parses feature entries from a configuration section.
    /// Handles array form (string/object elements) and object-map form (named properties with settings objects).
    /// Rejects ambiguous mixed-shape sections.
    /// </summary>
    public static List<FeatureEntry> ParseFeaturesFromConfiguration(IConfigurationSection featuresSection, string? shellName = null)
    {
        var shape = DetectFeaturesShape(featuresSection);

        var entries = shape switch
        {
            FeaturesShape.Empty => [],
            FeaturesShape.Array => ParseArrayFeaturesFromConfiguration(featuresSection, shellName),
            FeaturesShape.Object => ParseObjectMapFeaturesFromConfiguration(featuresSection, shellName),
            FeaturesShape.Ambiguous => throw new InvalidOperationException(
                $"Shell '{shellName ?? "unknown"}' has an ambiguous 'Features' section that mixes array and object-map children. " +
                "Use either array syntax or object-map syntax, not both."),
            _ => throw new InvalidOperationException(
                $"Shell '{shellName ?? "unknown"}' has an unrecognized 'Features' configuration shape '{shape}'."),
        };

        ValidateNoDuplicateFeatures(entries, shellName ?? "unknown");
        return entries;
    }

    private static List<FeatureEntry> ParseArrayFeaturesFromConfiguration(IConfigurationSection featuresSection, string? shellName = null)
    {
        var entries = new List<FeatureEntry>();
        var shellContext = shellName is not null ? $" in shell '{shellName}'" : "";

        foreach (var featureSection in featuresSection.GetChildren())
        {
            // Check if this is a simple string value
            if (!featureSection.GetChildren().Any())
            {
                var name = featureSection.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException(
                        $"Feature array entry '{featureSection.Key}'{shellContext} must define a non-empty feature name.");
                }

                entries.Add(FeatureEntry.FromName(name.Trim()));
            }
            else
            {
                // This is an object with Name and settings
                var name = featureSection["Name"];
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException(
                        $"Feature array entry '{featureSection.Key}'{shellContext} must define a non-empty 'Name' property.");
                }

                var entry = new FeatureEntry { Name = name.Trim() };
                var children = featureSection.GetChildren().ToList();
                var settingsWrapper = children.FirstOrDefault(c => c.Key.Equals("Settings", StringComparison.OrdinalIgnoreCase));
                var directSettings = children
                    .Where(c => !c.Key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    .Where(c => !c.Key.Equals("Settings", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (settingsWrapper is not null)
                {
                    var settingsChildren = settingsWrapper.GetChildren().ToList();
                    if (settingsWrapper.Value is not null && settingsChildren.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Feature '{entry.Name}'{shellContext} uses a 'Settings' wrapper that must contain an object value.");
                    }

                    if (directSettings.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Feature '{entry.Name}'{shellContext} mixes the 'Settings' wrapper with direct settings. Use one feature settings style.");
                    }

                    PopulateEntrySettings(entry, settingsChildren);
                }
                else
                {
                    PopulateEntrySettings(entry, directSettings);
                }

                entries.Add(entry);
            }
        }

        return entries;
    }

    private static List<FeatureEntry> ParseObjectMapFeaturesFromConfiguration(IConfigurationSection featuresSection, string? shellName = null)
    {
        var entries = new List<FeatureEntry>();
        var shellContext = shellName is not null ? $" in shell '{shellName}'" : "";

        foreach (var featureSection in featuresSection.GetChildren())
        {
            var featureName = featureSection.Key;

            if (string.IsNullOrWhiteSpace(featureName))
            {
                throw new InvalidOperationException(
                    $"Feature name{shellContext} in object-map syntax must not be empty or whitespace.");
            }

            var children = featureSection.GetChildren().ToList();

            if (featureSection.Value is not null)
            {
                if (TryParseFeatureBoolean(featureSection.Value, out var enabled))
                {
                    entries.Add(enabled
                        ? FeatureEntry.EnableDefaults(featureName.Trim())
                        : FeatureEntry.Disabled(featureName.Trim()));
                    continue;
                }

                throw new InvalidOperationException(
                    $"Feature '{featureName}'{shellContext} in object-map syntax must be {SupportedFeatureValueForms}, but found scalar value '{featureSection.Value}'.");
            }

            if (children.Count == 0)
            {
                entries.Add(FeatureEntry.FromName(featureName.Trim()));
                continue;
            }

            // Reject array-like children (e.g., "Posts": [1, 2])
            if (children.Count > 0 && children.All(c => int.TryParse(c.Key, out _)))
            {
                throw new InvalidOperationException(
                    $"Feature '{featureName}'{shellContext} in object-map syntax must be {SupportedFeatureValueForms}, but found an array.");
            }

            var entry = new FeatureEntry { Name = featureName.Trim() };

            // In object-map form, all children (including "Name") are settings
            foreach (var settingSection in children)
            {
                if (settingSection.GetChildren().Any())
                {
                    var json = SerializeConfigurationSection(settingSection);
                    entry.Settings[settingSection.Key] = JsonSerializer.Deserialize<JsonElement>(json);
                }
                else
                {
                    entry.Settings[settingSection.Key] = settingSection.Value;
                }
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static bool TryParseFeatureBoolean(string value, out bool enabled)
    {
        if (value.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (value.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }

    private static void EnsureFeatureName(string name, string context)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"{context} must define a non-empty feature name.");
        }
    }

    private static IReadOnlyList<string> AddName(IReadOnlyList<string> source, string name)
    {
        var names = source.ToList();
        if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            names.Add(name);
        return [..names];
    }

    private static IReadOnlyList<string> RemoveName(IReadOnlyList<string> source, string name) =>
        [..source.Where(candidate => !candidate.Equals(name, StringComparison.OrdinalIgnoreCase))];

    private static void PopulateEntrySettings(FeatureEntry entry, IEnumerable<IConfigurationSection> settingSections)
    {
        foreach (var settingSection in settingSections)
        {
            if (settingSection.GetChildren().Any())
            {
                var json = SerializeConfigurationSection(settingSection);
                entry.Settings[settingSection.Key] = JsonSerializer.Deserialize<JsonElement>(json);
            }
            else
            {
                entry.Settings[settingSection.Key] = settingSection.Value;
            }
        }
    }
}
