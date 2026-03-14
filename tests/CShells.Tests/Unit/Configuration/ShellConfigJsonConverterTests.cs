using System.Text.Json;
using CShells.Configuration;

namespace CShells.Tests.Unit.Configuration;

public class ShellConfigJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FeatureEntryListJsonConverter() }
    };

    [Fact(DisplayName = "ShellConfig round-trip with object-map Features preserves features")]
    public void RoundTrip_ObjectMapFeatures_PreservesFeatures()
    {
        // Arrange
        var json = """
        {
            "Name": "Default",
            "Features": {
                "Core": {},
                "Posts": {},
                "Analytics": { "TopPostsCount": 10 }
            }
        }
        """;

        // Act — deserialize
        var config = JsonSerializer.Deserialize<ShellConfig>(json, Options);
        Assert.NotNull(config);

        // Act — serialize back
        var serialized = JsonSerializer.Serialize(config, Options);
        var roundTripped = JsonSerializer.Deserialize<ShellConfig>(serialized, Options);

        // Assert
        Assert.NotNull(roundTripped);
        Assert.Equal("Default", roundTripped.Name);
        Assert.Equal(3, roundTripped.Features.Count);
        Assert.Equal("Core", roundTripped.Features[0].Name);
        Assert.Equal("Posts", roundTripped.Features[1].Name);
        Assert.Equal("Analytics", roundTripped.Features[2].Name);
        Assert.Equal(10, roundTripped.Features[2].Settings["TopPostsCount"]);
    }

    [Fact(DisplayName = "ShellConfig round-trip with array Features preserves features")]
    public void RoundTrip_ArrayFeatures_PreservesFeatures()
    {
        // Arrange
        var json = """
        {
            "Name": "Default",
            "Features": [
                "Core",
                { "Name": "Analytics", "TopPostsCount": 10 }
            ]
        }
        """;

        // Act — deserialize
        var config = JsonSerializer.Deserialize<ShellConfig>(json, Options);
        Assert.NotNull(config);

        // Act — serialize back (now outputs object-map form)
        var serialized = JsonSerializer.Serialize(config, Options);
        var roundTripped = JsonSerializer.Deserialize<ShellConfig>(serialized, Options);

        // Assert
        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped.Features.Count);
        Assert.Equal("Core", roundTripped.Features[0].Name);
        Assert.Equal("Analytics", roundTripped.Features[1].Name);
        Assert.Equal(10, roundTripped.Features[1].Settings["TopPostsCount"]);
    }

    [Fact(DisplayName = "ShellConfig serialization prefers object-map output")]
    public void Serialization_PrefersObjectMapOutput()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features =
            [
                FeatureEntry.FromName("Core"),
                new() { Name = "Analytics", Settings = new() { ["TopPostsCount"] = 10 } }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(config, Options);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var features = doc.RootElement.GetProperty("Features");
        Assert.Equal(JsonValueKind.Object, features.ValueKind);
        Assert.True(features.TryGetProperty("Core", out _));
        Assert.True(features.TryGetProperty("Analytics", out _));
    }

    [Fact(DisplayName = "ShellConfig serialization emits empty objects for features without settings")]
    public void Serialization_EmitsEmptyObjects_ForFeaturesWithoutSettings()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features = [FeatureEntry.FromName("Core"), FeatureEntry.FromName("Posts")]
        };

        // Act
        var json = JsonSerializer.Serialize(config, Options);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var features = doc.RootElement.GetProperty("Features");
        Assert.Equal("{}", features.GetProperty("Core").GetRawText());
        Assert.Equal("{}", features.GetProperty("Posts").GetRawText());
    }

    [Fact(DisplayName = "ShellConfig deserialization handles object-map with nested settings")]
    public void Deserialization_ObjectMap_NestedSettings()
    {
        // Arrange
        var json = """
        {
            "Name": "TestShell",
            "Features": {
                "Database": {
                    "Connection": {
                        "Server": "localhost",
                        "Port": 5432
                    }
                }
            }
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<ShellConfig>(json, Options);

        // Assert
        Assert.NotNull(config);
        Assert.Single(config.Features);
        Assert.Equal("Database", config.Features[0].Name);
        var connection = Assert.IsType<JsonElement>(config.Features[0].Settings["Connection"]);
        Assert.Equal("localhost", connection.GetProperty("Server").GetString());
        Assert.Equal(5432, connection.GetProperty("Port").GetInt32());
    }

    [Fact(DisplayName = "ShellConfig serialization rejects duplicate feature names")]
    public void Serialization_RejectsDuplicateFeatureNames()
    {
        // Arrange — a ShellConfig with duplicate feature names
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features =
            [
                FeatureEntry.FromName("Core"),
                FeatureEntry.FromName("Analytics"),
                FeatureEntry.FromName("Core") // duplicate
            ]
        };

        // Act & Assert
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(config, Options));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core", ex.Message);
    }

    [Fact(DisplayName = "ShellConfig deserialization of array with duplicate features preserves both")]
    public void Deserialization_ArrayWithDuplicates_PreservesBoth()
    {
        // Arrange — JSON array with duplicates (JSON is valid; validation happens downstream)
        var json = """
        {
            "Name": "TestShell",
            "Features": ["Core", "Core"]
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<ShellConfig>(json, Options);

        // Assert — deserialization doesn't reject; runtime validation does
        Assert.NotNull(config);
        Assert.Equal(2, config.Features.Count);
        Assert.All(config.Features, f => Assert.Equal("Core", f.Name));
    }

    [Fact(DisplayName = "ShellConfig deserialization converts both shapes to same model")]
    public void Deserialization_BothShapes_ProduceSameModel()
    {
        // Arrange
        var arrayJson = """
        {
            "Name": "Shell",
            "Features": [
                "Core",
                { "Name": "Analytics", "TopPostsCount": 10 }
            ]
        }
        """;

        var objectMapJson = """
        {
            "Name": "Shell",
            "Features": {
                "Core": {},
                "Analytics": { "TopPostsCount": 10 }
            }
        }
        """;

        // Act
        var arrayConfig = JsonSerializer.Deserialize<ShellConfig>(arrayJson, Options);
        var objectMapConfig = JsonSerializer.Deserialize<ShellConfig>(objectMapJson, Options);

        // Assert
        Assert.NotNull(arrayConfig);
        Assert.NotNull(objectMapConfig);
        Assert.Equal(arrayConfig.Features.Count, objectMapConfig.Features.Count);

        for (var i = 0; i < arrayConfig.Features.Count; i++)
        {
            Assert.Equal(arrayConfig.Features[i].Name, objectMapConfig.Features[i].Name);
            Assert.Equal(
                arrayConfig.Features[i].Settings.Keys.OrderBy(k => k),
                objectMapConfig.Features[i].Settings.Keys.OrderBy(k => k));
        }
    }
}
