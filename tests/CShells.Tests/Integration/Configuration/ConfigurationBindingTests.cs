using System.Text;
using Microsoft.Extensions.Configuration;
using CShells.Configuration;

namespace CShells.Tests.Configuration
{
    public class ConfigurationBindingTests
    {
        [Fact]
        public void ShellSettingsFactory_CreatesSettings_FromValidJson()
        {
            var json = @"{
              ""CShells"": {
                ""Shells"": [
                  {
                    ""Name"": ""Default"",
                    ""Features"": [ ""Core"", ""Weather"" ],
                    ""Configuration"": { ""Title"": ""Default Shell"" }
                  },
                  {
                    ""Name"": ""Admin"",
                    ""Features"": [ ""Core"", ""Admin"" ],
                    ""Configuration"": { ""Title"": ""Admin Shell"" }
                  }
                ]
              }
            }";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var config = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();

            // Use CreateFromConfiguration to properly parse mixed feature arrays
            var shellsSection = config.GetSection("CShells").GetSection("Shells");
            var settings = shellsSection.GetChildren()
                .Select(ShellSettingsFactory.CreateFromConfiguration)
                .ToList();

            Assert.Equal(2, settings.Count);
            Assert.Contains(settings, s => s.Id.Name == "Default");
            Assert.Contains(settings, s => s.Id.Name == "Admin");

            var def = settings.First(s => s.Id.Name == "Default");
            Assert.Equal(["Core", "Weather"], def.EnabledFeatures);

            // Configuration is now flattened into ConfigurationData
            Assert.True(def.ConfigurationData.ContainsKey("Title"));
            Assert.Equal("Default Shell", def.ConfigurationData["Title"]);
        }

        // Note: Removed test "AddCShells_Registers_IShellHost_And_ShellSettings" because it tested
        // implementation details that changed with the move to endpoint routing. Shells are now
        // loaded when MapCShells() is called, not when services are registered.

        [Fact]
        public void CreateFromConfiguration_DetectsDuplicateNames()
        {
            var json = @"{ ""CShells"": { ""Shells"": [ { ""Name"": ""X"" }, { ""Name"": ""x"" } ] } }";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var config = new ConfigurationBuilder().AddJsonStream(stream).Build();

            // Use CreateFromConfiguration for proper parsing
            var shellsSection = config.GetSection("CShells").GetSection("Shells");
            var shellConfigs = shellsSection.GetChildren()
                .Select(ShellSettingsFactory.CreateFromConfiguration)
                .ToList();

            // Check for duplicates manually
            var duplicates = shellConfigs
                .GroupBy(s => s.Id.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            Assert.NotEmpty(duplicates);
            Assert.Contains("X", duplicates, StringComparer.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "Object-map Features loaded from IConfiguration produces correct feature names")]
        public void ObjectMapFeatures_LoadedFromConfiguration_ProducesCorrectFeatureNames()
        {
            var settings = LoadShell("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "Contoso",
                    "Features": {
                      "Core": {},
                      "Posts": {},
                      "Analytics": { "TopPostsCount": 10 }
                    }
                  }
                ]
              }
            }
            """);

            Assert.Equal("Contoso", settings.Id.Name);
            // IConfiguration sorts children alphabetically, so order differs from declaration
            Assert.Equal(3, settings.EnabledFeatures.Count);
            Assert.Contains("Core", settings.EnabledFeatures);
            Assert.Contains("Posts", settings.EnabledFeatures);
            Assert.Contains("Analytics", settings.EnabledFeatures);
        }

        [Fact(DisplayName = "Object-map Features through IConfiguration returns all declared features")]
        public void ObjectMapFeatures_ThroughIConfiguration_ReturnsAllDeclaredFeatures()
        {
            // IConfiguration sorts children alphabetically by key, so object-map
            // property order is not preserved through this path. Declaration
            // order is preserved only through direct JSON deserialization.
            var settings = LoadShell("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "OrderTest",
                    "Features": {
                      "Zeta": {},
                      "Alpha": {},
                      "Mu": {}
                    }
                  }
                ]
              }
            }
            """);

            Assert.Equal(3, settings.EnabledFeatures.Count);
            Assert.Contains("Zeta", settings.EnabledFeatures);
            Assert.Contains("Alpha", settings.EnabledFeatures);
            Assert.Contains("Mu", settings.EnabledFeatures);
        }

        [Fact(DisplayName = "Object-map Features flattens feature settings into ConfigurationData")]
        public void ObjectMapFeatures_FlattensFeatureSettings()
        {
            var settings = LoadShell("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "SettingsTest",
                    "Features": {
                      "Core": {},
                      "Analytics": { "TopPostsCount": "10", "Enabled": "true" }
                    }
                  }
                ]
              }
            }
            """);

            Assert.Equal("10", settings.ConfigurationData["Analytics:TopPostsCount"]);
            Assert.Equal("true", settings.ConfigurationData["Analytics:Enabled"]);
        }

        [Fact(DisplayName = "Object-map and array Features produce equivalent ConfigurationData")]
        public void ObjectMapAndArray_ProduceEquivalentConfigurationData()
        {
            var arraySettings = LoadShell("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "Shell1",
                    "Features": [
                      "Core",
                      { "Name": "Analytics", "TopPostsCount": "10" }
                    ]
                  }
                ]
              }
            }
            """);

            var objectMapSettings = LoadShell("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "Shell1",
                    "Features": {
                      "Core": {},
                      "Analytics": { "TopPostsCount": "10" }
                    }
                  }
                ]
              }
            }
            """);

            // Feature names may differ in order (IConfiguration sorts alphabetically for object-map)
            // but should contain the same set
            Assert.Equal(
                arraySettings.EnabledFeatures.OrderBy(f => f).ToArray(),
                objectMapSettings.EnabledFeatures.OrderBy(f => f).ToArray());

            Assert.Equal(
                arraySettings.ConfigurationData.OrderBy(kv => kv.Key).ToArray(),
                objectMapSettings.ConfigurationData.OrderBy(kv => kv.Key).ToArray());
        }

        [Fact(DisplayName = "Object-map Features with nested settings flattens nested properties")]
        public void ObjectMapFeatures_WithNestedSettings_FlattensNestedProperties()
        {
            var settings = LoadShell("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "NestedTest",
                    "Features": {
                      "Database": {
                        "Connection": {
                          "Server": "localhost",
                          "Port": "5432"
                        }
                      }
                    }
                  }
                ]
              }
            }
            """);

            Assert.Equal(["Database"], settings.EnabledFeatures);
            Assert.Equal("localhost", settings.ConfigurationData["Database:Connection:Server"]);
            Assert.Equal("5432", settings.ConfigurationData["Database:Connection:Port"]);
        }

        [Fact(DisplayName = "Empty object-map Features produces no enabled features")]
        public void EmptyObjectMapFeatures_ProducesNoEnabledFeatures()
        {
            var settings = LoadShell("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "EmptyTest",
                    "Features": {}
                  }
                ]
              }
            }
            """);

            Assert.Empty(settings.EnabledFeatures);
        }

        [Fact(DisplayName = "Duplicate features in array form throws with shell context")]
        public void DuplicateFeatures_InArrayForm_ThrowsWithShellContext()
        {
            var section = ShellSection("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "DupShell",
                    "Features": ["Core", "Analytics", "Core"]
                  }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(
                () => ShellSettingsFactory.CreateFromConfiguration(section));
            Assert.Contains("DupShell", ex.Message);
            Assert.Contains("Core", ex.Message);
            Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "Duplicate features in object array form throws with shell context")]
        public void DuplicateFeatures_InObjectArrayForm_ThrowsWithShellContext()
        {
            var section = ShellSection("""
            {
              "CShells": {
                "Shells": [
                  {
                    "Name": "DupShell2",
                    "Features": [
                      { "Name": "Core" },
                      { "Name": "Analytics" },
                      { "Name": "Core" }
                    ]
                  }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(
                () => ShellSettingsFactory.CreateFromConfiguration(section));
            Assert.Contains("DupShell2", ex.Message);
            Assert.Contains("Core", ex.Message);
        }

        [Fact(DisplayName = "Mixed array and object-map Features throws ambiguous error")]
        public void MixedShapeFeatures_ThrowsAmbiguousError()
        {
            // IConfiguration can't natively produce mixed shapes from a single JSON file,
            // but multiple configuration providers could merge sections with numeric and named keys.
            // Simulate by directly building an IConfiguration with mixed keys.
            var configData = new Dictionary<string, string?>
            {
                ["CShells:Shells:0:Name"] = "MixedShell",
                ["CShells:Shells:0:Features:0"] = "Core",           // numeric key (array-like)
                ["CShells:Shells:0:Features:Analytics:TopPostsCount"] = "10" // named key (object-map-like)
            };

            var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
            var shellSection = config.GetSection("CShells:Shells:0");

            var ex = Assert.Throws<InvalidOperationException>(
                () => ShellSettingsFactory.CreateFromConfiguration(shellSection));
            Assert.Contains("MixedShell", ex.Message);
            Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        [Fact(DisplayName = "Object-map feature with scalar value throws with feature context")]
        public void ObjectMapFeature_ScalarValue_ThrowsWithFeatureContext()
        {
            var configData = new Dictionary<string, string?>
            {
                ["CShells:Shells:0:Name"] = "ScalarShell",
                ["CShells:Shells:0:Features:Core"] = null,
                ["CShells:Shells:0:Features:Posts"] = "invalid"  // scalar value
            };

            var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
            var shellSection = config.GetSection("CShells:Shells:0");

            var ex = Assert.Throws<InvalidOperationException>(
                () => ShellSettingsFactory.CreateFromConfiguration(shellSection));
            Assert.Contains("Posts", ex.Message);
            Assert.Contains("scalar", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "Object-map feature with array value throws with feature context")]
        public void ObjectMapFeature_ArrayValue_ThrowsWithFeatureContext()
        {
            var configData = new Dictionary<string, string?>
            {
                ["CShells:Shells:0:Name"] = "ArrayShell",
                ["CShells:Shells:0:Features:Core:TopCount"] = "5",  // valid object-map feature
                ["CShells:Shells:0:Features:Posts:0"] = "tag1",     // array-like children
                ["CShells:Shells:0:Features:Posts:1"] = "tag2"
            };

            var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
            var shellSection = config.GetSection("CShells:Shells:0");

            var ex = Assert.Throws<InvalidOperationException>(
                () => ShellSettingsFactory.CreateFromConfiguration(shellSection));
            Assert.Contains("Posts", ex.Message);
            Assert.Contains("array", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static IConfigurationSection ShellSection(string json, int index = 0)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return new ConfigurationBuilder().AddJsonStream(stream).Build()
                .GetSection($"CShells:Shells:{index}");
        }

        private static ShellSettings LoadShell(string json, int index = 0) =>
            ShellSettingsFactory.CreateFromConfiguration(ShellSection(json, index));
    }
}
