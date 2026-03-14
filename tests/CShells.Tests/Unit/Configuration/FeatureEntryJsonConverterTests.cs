using System.Text.Json;
using CShells.Configuration;

namespace CShells.Tests.Unit.Configuration;

public class FeatureEntryJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new FeatureEntryJsonConverter() }
    };

    [Fact(DisplayName = "Deserialize string feature entry")]
    public void Deserialize_StringFeatureEntry_ReturnsFeatureEntryWithName()
    {
        // Arrange
        var json = "\"Core\"";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("Core", entry.Name);
        Assert.Empty(entry.Settings);
    }

    [Fact(DisplayName = "Deserialize object feature entry with settings")]
    public void Deserialize_ObjectFeatureEntry_ReturnsFeatureEntryWithSettings()
    {
        // Arrange
        var json = """{ "Name": "FraudDetection", "Threshold": 0.85, "MaxAmount": 5000 }""";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("FraudDetection", entry.Name);
        Assert.Equal(2, entry.Settings.Count);
        Assert.Equal(0.85, entry.Settings["Threshold"]);
        Assert.Equal(5000, entry.Settings["MaxAmount"]);
    }

    [Fact(DisplayName = "Deserialize object feature entry with nested settings")]
    public void Deserialize_ObjectFeatureEntryWithNestedSettings_PreservesStructure()
    {
        // Arrange
        var json = """{ "Name": "Database", "Connection": { "Server": "localhost", "Port": 5432 } }""";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("Database", entry.Name);
        Assert.Single(entry.Settings);
        var connectionElement = Assert.IsType<JsonElement>(entry.Settings["Connection"]);
        Assert.Equal("localhost", connectionElement.GetProperty("Server").GetString());
        Assert.Equal(5432, connectionElement.GetProperty("Port").GetInt32());
    }

    [Fact(DisplayName = "Deserialize array of mixed feature entries")]
    public void Deserialize_MixedArray_ReturnsCorrectEntries()
    {
        // Arrange
        var json = """["Core", { "Name": "FraudDetection", "Threshold": 0.85 }, "Logging"]""";
        var options = new JsonSerializerOptions
        {
            Converters = { new FeatureEntryListJsonConverter() }
        };

        // Act
        var entries = JsonSerializer.Deserialize<List<FeatureEntry>>(json, options);

        // Assert
        Assert.NotNull(entries);
        Assert.Equal(3, entries.Count);

        Assert.Equal("Core", entries[0].Name);
        Assert.Empty(entries[0].Settings);

        Assert.Equal("FraudDetection", entries[1].Name);
        Assert.Single(entries[1].Settings);
        Assert.Equal(0.85, entries[1].Settings["Threshold"]);

        Assert.Equal("Logging", entries[2].Name);
        Assert.Empty(entries[2].Settings);
    }

    [Fact(DisplayName = "Serialize feature entry without settings as string")]
    public void Serialize_FeatureEntryWithoutSettings_ReturnsString()
    {
        // Arrange
        var entry = FeatureEntry.FromName("Core");

        // Act
        var json = JsonSerializer.Serialize(entry, Options);

        // Assert
        Assert.Equal("\"Core\"", json);
    }

    [Fact(DisplayName = "Serialize feature entry with settings as object")]
    public void Serialize_FeatureEntryWithSettings_ReturnsObject()
    {
        // Arrange
        var entry = new FeatureEntry
        {
            Name = "FraudDetection",
            Settings = new() { ["Threshold"] = 0.85 }
        };

        // Act
        var json = JsonSerializer.Serialize(entry, Options);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("FraudDetection", root.GetProperty("Name").GetString());
        Assert.Equal(0.85, root.GetProperty("Threshold").GetDouble());
    }

    [Fact(DisplayName = "Deserialize trims whitespace from feature names")]
    public void Deserialize_TrimsWhitespace_FromFeatureNames()
    {
        // Arrange
        var json = "\"  Core  \"";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("Core", entry.Name);
    }

    [Fact(DisplayName = "Deserialize throws for empty feature name")]
    public void Deserialize_EmptyFeatureName_ThrowsJsonException()
    {
        // Arrange
        var json = "\"\"";

        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureEntry>(json, Options));
    }

    [Fact(DisplayName = "Deserialize throws for object without Name property")]
    public void Deserialize_ObjectWithoutName_ThrowsJsonException()
    {
        // Arrange
        var json = """{ "Threshold": 0.85 }""";

        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureEntry>(json, Options));
    }

    [Fact(DisplayName = "Deserialize handles case-insensitive Name property")]
    public void Deserialize_CaseInsensitiveName_Works()
    {
        // Arrange
        var json = """{ "name": "FraudDetection", "Threshold": 0.85 }""";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("FraudDetection", entry.Name);
    }

    [Fact(DisplayName = "Deserialize handles boolean settings")]
    public void Deserialize_BooleanSettings_Works()
    {
        // Arrange
        var json = """{ "Name": "Feature", "Enabled": true, "Debug": false }""";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(true, entry.Settings["Enabled"]);
        Assert.Equal(false, entry.Settings["Debug"]);
    }

    [Fact(DisplayName = "Deserialize handles string settings")]
    public void Deserialize_StringSettings_Works()
    {
        // Arrange
        var json = """{ "Name": "Database", "ConnectionString": "Server=localhost" }""";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("Server=localhost", entry.Settings["ConnectionString"]);
    }

    [Fact(DisplayName = "Deserialize handles array settings")]
    public void Deserialize_ArraySettings_Works()
    {
        // Arrange
        var json = """{ "Name": "Feature", "Tags": ["tag1", "tag2"] }""";

        // Act
        var entry = JsonSerializer.Deserialize<FeatureEntry>(json, Options);

        // Assert
        Assert.NotNull(entry);
        var tagsElement = Assert.IsType<JsonElement>(entry.Settings["Tags"]);
        Assert.Equal(JsonValueKind.Array, tagsElement.ValueKind);
        Assert.Equal(2, tagsElement.GetArrayLength());
    }

    // --- FeatureEntryListJsonConverter dual-shape tests ---

    private static readonly JsonSerializerOptions ListOptions = new()
    {
        Converters = { new FeatureEntryListJsonConverter() }
    };

    [Fact(DisplayName = "List converter deserializes object-map Features")]
    public void ListConverter_Deserialize_ObjectMapFeatures()
    {
        // Arrange
        var json = """{ "Core": {}, "Posts": {}, "Analytics": { "TopPostsCount": 10 } }""";

        // Act
        var entries = JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions);

        // Assert
        Assert.NotNull(entries);
        Assert.Equal(3, entries.Count);
        Assert.Equal("Core", entries[0].Name);
        Assert.Empty(entries[0].Settings);
        Assert.Equal("Posts", entries[1].Name);
        Assert.Empty(entries[1].Settings);
        Assert.Equal("Analytics", entries[2].Name);
        Assert.Equal(10, entries[2].Settings["TopPostsCount"]);
    }

    [Fact(DisplayName = "List converter preserves object-map declaration order")]
    public void ListConverter_Deserialize_PreservesObjectMapOrder()
    {
        // Arrange
        var json = """{ "Zeta": {}, "Alpha": {}, "Mu": {} }""";

        // Act
        var entries = JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions);

        // Assert
        Assert.NotNull(entries);
        Assert.Equal(["Zeta", "Alpha", "Mu"], entries.Select(e => e.Name).ToArray());
    }

    [Fact(DisplayName = "List converter deserializes array Features unchanged")]
    public void ListConverter_Deserialize_ArrayFeatures_Unchanged()
    {
        // Arrange
        var json = """["Core", { "Name": "Analytics", "TopPostsCount": 10 }]""";

        // Act
        var entries = JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions);

        // Assert
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.Equal("Core", entries[0].Name);
        Assert.Equal("Analytics", entries[1].Name);
        Assert.Equal(10, entries[1].Settings["TopPostsCount"]);
    }

    [Fact(DisplayName = "List converter serializes features as object-map")]
    public void ListConverter_Serialize_ProducesObjectMap()
    {
        // Arrange
        var entries = new List<FeatureEntry>
        {
            FeatureEntry.FromName("Core"),
            new() { Name = "Analytics", Settings = new() { ["TopPostsCount"] = 10 } }
        };

        // Act
        var json = JsonSerializer.Serialize(entries, ListOptions);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("Core", out var coreValue));
        Assert.Equal(JsonValueKind.Object, coreValue.ValueKind);
        Assert.Empty(coreValue.EnumerateObject().ToList());
        Assert.True(root.TryGetProperty("Analytics", out var analyticsValue));
        Assert.Equal(10, analyticsValue.GetProperty("TopPostsCount").GetInt32());
    }

    [Fact(DisplayName = "List converter serializes features without settings as empty objects")]
    public void ListConverter_Serialize_EmptySettingsAsEmptyObjects()
    {
        // Arrange
        var entries = new List<FeatureEntry>
        {
            FeatureEntry.FromName("Core"),
            FeatureEntry.FromName("Posts")
        };

        // Act
        var json = JsonSerializer.Serialize(entries, ListOptions);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("{}", root.GetProperty("Core").GetRawText());
        Assert.Equal("{}", root.GetProperty("Posts").GetRawText());
    }

    [Fact(DisplayName = "List converter rejects object-map entry with non-object value")]
    public void ListConverter_Deserialize_RejectsNonObjectMapValue()
    {
        // Arrange
        var json = """{ "Core": {}, "Posts": "invalid" }""";

        // Act & Assert
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions));
        Assert.Contains("Posts", ex.Message);
    }

    [Fact(DisplayName = "List converter rejects object-map entry with null value")]
    public void ListConverter_Deserialize_RejectsNullMapValue()
    {
        // Arrange
        var json = """{ "Core": {}, "Posts": null }""";

        // Act & Assert
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions));
        Assert.Contains("Posts", ex.Message);
    }

    [Fact(DisplayName = "List converter rejects object-map entry with array value")]
    public void ListConverter_Deserialize_RejectsArrayMapValue()
    {
        // Arrange
        var json = """{ "Core": {}, "Posts": [1, 2, 3] }""";

        // Act & Assert
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions));
        Assert.Contains("Posts", ex.Message);
    }

    [Fact(DisplayName = "List converter treats inner Name property as configuration in object-map")]
    public void ListConverter_Deserialize_InnerNameIsConfiguration()
    {
        // Arrange — in object-map form, Name inside a feature object is a setting, not identity
        var json = """{ "Analytics": { "Name": "AnalyticsDisplay", "TopPostsCount": 5 } }""";

        // Act
        var entries = JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions);

        // Assert
        Assert.NotNull(entries);
        Assert.Single(entries);
        Assert.Equal("Analytics", entries[0].Name);
        Assert.Equal("AnalyticsDisplay", entries[0].Settings["Name"]);
        Assert.Equal(5, entries[0].Settings["TopPostsCount"]);
    }

    [Fact(DisplayName = "List converter deserializes empty object-map as empty list")]
    public void ListConverter_Deserialize_EmptyObjectMap_ReturnsEmptyList()
    {
        // Arrange
        var json = "{}";

        // Act
        var entries = JsonSerializer.Deserialize<List<FeatureEntry>>(json, ListOptions);

        // Assert
        Assert.NotNull(entries);
        Assert.Empty(entries);
    }

    [Fact(DisplayName = "List converter serializes duplicate feature names throws")]
    public void ListConverter_Serialize_DuplicateFeatures_Throws()
    {
        // Arrange
        var entries = new List<FeatureEntry>
        {
            FeatureEntry.FromName("Core"),
            FeatureEntry.FromName("Core")
        };

        // Act & Assert
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Serialize(entries, ListOptions));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core", ex.Message);
    }
}

