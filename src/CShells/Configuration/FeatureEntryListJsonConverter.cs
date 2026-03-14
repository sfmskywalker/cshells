using System.Text.Json;
using System.Text.Json.Serialization;

namespace CShells.Configuration;

/// <summary>
/// JSON converter for <see cref="List{FeatureEntry}"/> that handles both array and object-map feature collections.
/// </summary>
public class FeatureEntryListJsonConverter : JsonConverter<List<FeatureEntry>>
{
    private static readonly FeatureEntryJsonConverter ItemConverter = new();

    /// <inheritdoc/>
    public override List<FeatureEntry> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            JsonTokenType.StartObject => ReadObjectMap(ref reader),
            _ => throw new JsonException($"Expected array or object for Features, but found {reader.TokenType}")
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, List<FeatureEntry> value, JsonSerializerOptions options)
    {
        // Prefer object-map output; duplicate names would lose data so we reject instead
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value)
        {
            if (!names.Add(entry.Name))
            {
                throw new JsonException(
                    $"Cannot serialize Features: duplicate configured feature name '{entry.Name}'.");
            }
        }

        writer.WriteStartObject();

        foreach (var entry in value)
        {
            writer.WritePropertyName(entry.Name);
            writer.WriteStartObject();

            foreach (var (key, settingValue) in entry.Settings)
            {
                writer.WritePropertyName(key);
                JsonSerializer.Serialize(writer, settingValue, options);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static List<FeatureEntry> ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var entries = new List<FeatureEntry>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            var entry = ItemConverter.Read(ref reader, typeof(FeatureEntry), options);
            entries.Add(entry);
        }

        return entries;
    }

    private static List<FeatureEntry> ReadObjectMap(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var entries = new List<FeatureEntry>();

        foreach (var property in root.EnumerateObject())
        {
            var featureName = property.Name.Trim();

            if (string.IsNullOrWhiteSpace(featureName))
            {
                throw new JsonException(
                    "Feature name in object-map syntax must not be empty or whitespace.");
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    $"Feature '{featureName}' in object-map syntax must have an object value, but found {property.Value.ValueKind}.");
            }

            var entry = new FeatureEntry { Name = featureName };

            foreach (var setting in property.Value.EnumerateObject())
            {
                entry.Settings[setting.Name] = CloneJsonElement(setting.Value);
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static object? CloneJsonElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => JsonSerializer.Deserialize<JsonElement>(element.GetRawText())
        };
}

