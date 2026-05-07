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

    [Fact(DisplayName = "CShellsOptions round-trip with shell map preserves shell keys and features")]
    public void CShellsOptions_RoundTrip_ShellMapPreservesKeysAndFeatures()
    {
        var json = """
        {
            "Shells": {
                "Default": {
                    "Features": {
                        "Core": {},
                        "Posts": {},
                        "Analytics": { "TopPostsCount": 10 }
                    }
                }
            }
        }
        """;

        var options = JsonSerializer.Deserialize<CShellsOptions>(json, Options);
        var serialized = JsonSerializer.Serialize(options, Options);
        var roundTripped = JsonSerializer.Deserialize<CShellsOptions>(serialized, Options);

        Assert.NotNull(roundTripped);
        var shell = Assert.Single(roundTripped.Shells, pair => pair.Key == "Default").Value;
        Assert.Equal(3, shell.Features.Count);
        Assert.Equal("Core", shell.Features[0].Name);
        Assert.Equal("Posts", shell.Features[1].Name);
        Assert.Equal("Analytics", shell.Features[2].Name);
        Assert.Equal(10, shell.Features[2].Settings["TopPostsCount"]);
    }

    [Fact(DisplayName = "ShellConfig round-trip with array Features preserves features")]
    public void RoundTrip_ArrayFeatures_PreservesFeatures()
    {
        var json = """
        {
            "Features": [
                "Core",
                { "Name": "Analytics", "TopPostsCount": 10 }
            ]
        }
        """;

        var config = Deserialize(json);
        var serialized = JsonSerializer.Serialize(config, Options);
        var roundTripped = Deserialize(serialized);

        Assert.Equal(2, roundTripped.Features.Count);
        Assert.Equal("Core", roundTripped.Features[0].Name);
        Assert.Equal("Analytics", roundTripped.Features[1].Name);
        Assert.Equal(10, roundTripped.Features[1].Settings["TopPostsCount"]);
    }

    [Fact(DisplayName = "ShellConfig serialization prefers object-map output")]
    public void Serialization_PrefersObjectMapOutput()
    {
        var config = new ShellConfig
        {
            Features =
            [
                FeatureEntry.FromName("Core"),
                new() { Name = "Analytics", Settings = new() { ["TopPostsCount"] = 10 } }
            ]
        };

        var json = JsonSerializer.Serialize(config, Options);

        var features = ParseFeatures(json);
        Assert.Equal(JsonValueKind.Object, features.ValueKind);
        Assert.True(features.TryGetProperty("Core", out _));
        Assert.True(features.TryGetProperty("Analytics", out _));
    }

    [Fact(DisplayName = "ShellConfig serialization emits empty objects for features without settings")]
    public void Serialization_EmitsEmptyObjects_ForFeaturesWithoutSettings()
    {
        var config = new ShellConfig
        {
            Features = [FeatureEntry.FromName("Core"), FeatureEntry.FromName("Posts")]
        };

        var json = JsonSerializer.Serialize(config, Options);

        var features = ParseFeatures(json);
        Assert.Equal("{}", features.GetProperty("Core").GetRawText());
        Assert.Equal("{}", features.GetProperty("Posts").GetRawText());
    }

    [Fact(DisplayName = "ShellConfig deserialization handles object-map with nested settings")]
    public void Deserialization_ObjectMap_NestedSettings()
    {
        var json = """
        {
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

        var config = Deserialize(json);

        Assert.Single(config.Features);
        Assert.Equal("Database", config.Features[0].Name);
        var connection = Assert.IsType<JsonElement>(config.Features[0].Settings["Connection"]);
        Assert.Equal("localhost", connection.GetProperty("Server").GetString());
        Assert.Equal(5432, connection.GetProperty("Port").GetInt32());
    }

    [Fact(DisplayName = "ShellConfig serialization rejects duplicate feature names")]
    public void Serialization_RejectsDuplicateFeatureNames()
    {
        var config = new ShellConfig
        {
            Features =
            [
                FeatureEntry.FromName("Core"),
                FeatureEntry.FromName("Analytics"),
                FeatureEntry.FromName("Core")
            ]
        };

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(config, Options));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core", ex.Message);
    }

    [Fact(DisplayName = "ShellConfig deserialization of array with duplicate features preserves both")]
    public void Deserialization_ArrayWithDuplicates_PreservesBoth()
    {
        var json = """
        {
            "Features": ["Core", "Core"]
        }
        """;

        var config = Deserialize(json);

        Assert.Equal(2, config.Features.Count);
        Assert.All(config.Features, f => Assert.Equal("Core", f.Name));
    }

    [Fact(DisplayName = "ShellConfig deserialization converts both feature shapes to same model")]
    public void Deserialization_BothFeatureShapes_ProduceSameModel()
    {
        var arrayJson = """
        {
            "Features": [
                "Core",
                { "Name": "Analytics", "TopPostsCount": 10 }
            ]
        }
        """;

        var objectMapJson = """
        {
            "Features": {
                "Core": {},
                "Analytics": { "TopPostsCount": 10 }
            }
        }
        """;

        var arrayConfig = Deserialize(arrayJson);
        var objectMapConfig = Deserialize(objectMapJson);

        Assert.Equal(arrayConfig.Features.Count, objectMapConfig.Features.Count);

        for (var index = 0; index < arrayConfig.Features.Count; index++)
        {
            Assert.Equal(arrayConfig.Features[index].Name, objectMapConfig.Features[index].Name);
            Assert.Equal(
                arrayConfig.Features[index].Settings.Keys.OrderBy(k => k),
                objectMapConfig.Features[index].Settings.Keys.OrderBy(k => k));
        }
    }

    private static ShellConfig Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize<ShellConfig>(json, Options);
        Assert.NotNull(config);
        return config;
    }

    private static JsonElement ParseFeatures(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Features").Clone();
    }
}
