using CShells.Configuration;

namespace CShells.Tests.Unit;

public class ShellSettingsFactoryTests
{
    [Theory(DisplayName = "Factory guard clauses throw ArgumentNullException")]
    [InlineData(true)]
    [InlineData(false)]
    public void Factory_GuardClauses_ThrowArgumentNullException(bool callCreate)
    {
        // Act
        var exception = callCreate
            ? Assert.Throws<ArgumentNullException>(() => ShellSettingsFactory.Create(null!))
            : Assert.Throws<ArgumentNullException>(() => ShellSettingsFactory.CreateAll(null!));

        // Assert
        Assert.Equal(callCreate ? "config" : "options", exception.ParamName);
    }

    [Fact(DisplayName = "Create with valid config returns ShellSettings")]
    public void Create_WithValidConfig_ReturnsShellSettings()
    {
        // Arrange
        var config = BuildShellConfig();

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal("TestShell", settings.Id.Name);
        Assert.Equal(["Feature1", "Feature2"], settings.EnabledFeatures);
        Assert.Single(settings.ConfigurationData);
        Assert.Equal("Value1", settings.ConfigurationData["Key1"]);
    }

    [Fact(DisplayName = "Create with empty config returns ShellSettings with empty collections")]
    public void Create_WithEmptyConfig_ReturnsShellSettingsWithEmptyCollections()
    {
        // Arrange
        var config = new ShellConfig { Name = "EmptyShell" };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal("EmptyShell", settings.Id.Name);
        Assert.Empty(settings.EnabledFeatures);
        Assert.Empty(settings.ConfigurationData);
    }

    [Fact(DisplayName = "CreateAll with valid options returns ShellSettings collection")]
    public void CreateAll_WithValidOptions_ReturnsShellSettingsCollection()
    {
        // Arrange
        var options = new CShellsOptions
        {
            Shells =
            [
                new() { Name = "Shell1", Features = [Feature("Feature1")] },
                new() { Name = "Shell2", Features = [Feature("Feature2"), Feature("Feature3")] }
            ]
        };

        // Act
        var settingsList = ShellSettingsFactory.CreateAll(options);

        // Assert
        Assert.Equal(2, settingsList.Count);
        Assert.Equal("Shell1", settingsList[0].Id.Name);
        Assert.Equal(["Feature1"], settingsList[0].EnabledFeatures);
        Assert.Equal("Shell2", settingsList[1].Id.Name);
        Assert.Equal(["Feature2", "Feature3"], settingsList[1].EnabledFeatures);
    }

    [Fact(DisplayName = "Create normalizes feature names by trimming whitespace and filtering empty")]
    public void Create_NormalizesFeatureNames()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features = [Feature(" Feature1 "), Feature("Feature2"), Feature("  "), Feature("Feature3  ")]
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal(["Feature1", "Feature2", "Feature3"], settings.EnabledFeatures);
    }

    [Fact(DisplayName = "CreateAll with duplicate shell names throws ArgumentException")]
    public void CreateAll_WithDuplicateShellNames_ThrowsArgumentException()
    {
        // Arrange
        var options = new CShellsOptions
        {
            Shells =
            [
                new() { Name = "DuplicateShell" },
                new() { Name = "DuplicateShell" }
            ]
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => ShellSettingsFactory.CreateAll(options));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Create with feature settings populates ConfigurationData")]
    public void Create_WithFeatureSettings_PopulatesConfigurationData()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features =
            [
                new FeatureEntry
                {
                    Name = "FraudDetection",
                    Settings = new() { ["Threshold"] = 0.85, ["MaxAmount"] = 5000 }
                }
            ],
            Configuration = new() { ["Prop1"] = "PropValue1" }
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal("TestShell", settings.Id.Name);
        Assert.Equal(3, settings.ConfigurationData.Count); // Prop1 + 2 feature settings
        Assert.Equal("PropValue1", settings.ConfigurationData["Prop1"]);
        Assert.Equal(0.85, settings.ConfigurationData["FraudDetection:Threshold"]);
        Assert.Equal(5000, settings.ConfigurationData["FraudDetection:MaxAmount"]);
    }

    [Fact(DisplayName = "Create with mixed string and object features works correctly")]
    public void Create_WithMixedFeatures_WorksCorrectly()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features =
            [
                FeatureEntry.FromName("Core"),
                new FeatureEntry
                {
                    Name = "Database",
                    Settings = new() { ["ConnectionString"] = "Server=localhost" }
                },
                FeatureEntry.FromName("Logging")
            ]
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal(["Core", "Database", "Logging"], settings.EnabledFeatures);
        Assert.Single(settings.ConfigurationData);
        Assert.Equal("Server=localhost", settings.ConfigurationData["Database:ConnectionString"]);
    }

    [Fact(DisplayName = "Create ignores null settings values in feature entries")]
    public void Create_IgnoresNullSettingsValues()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features =
            [
                new FeatureEntry
                {
                    Name = "Feature1",
                    Settings = new()
                    {
                        ["Setting1"] = "Value1",
                        ["Setting2"] = null,
                        ["Setting3"] = "Value3"
                    }
                }
            ]
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal(2, settings.ConfigurationData.Count);
        Assert.Equal("Value1", settings.ConfigurationData["Feature1:Setting1"]);
        Assert.Equal("Value3", settings.ConfigurationData["Feature1:Setting3"]);
        Assert.False(settings.ConfigurationData.ContainsKey("Feature1:Setting2"));
    }

    private static ShellConfig BuildShellConfig() => new()
    {
        Name = "TestShell",
        Features = [Feature("Feature1"), Feature("Feature2")],
        Configuration = new() { ["Key1"] = "Value1" }
    };

    private static FeatureEntry Feature(string name) => FeatureEntry.FromName(name);
    private static List<FeatureEntry> FeatureList(params string[] names) => names.Select(Feature).ToList();

    [Fact(DisplayName = "Create with object-map features normalizes correctly")]
    public void Create_WithObjectMapFeatures_NormalizesCorrectly()
    {
        // Arrange — simulate what the list converter produces from object-map JSON
        var config = new ShellConfig
        {
            Name = "ObjMapShell",
            Features =
            [
                Feature("Core"),
                Feature("Posts"),
                new FeatureEntry
                {
                    Name = "Analytics",
                    Settings = new() { ["TopPostsCount"] = 10 }
                }
            ]
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal("ObjMapShell", settings.Id.Name);
        Assert.Equal(["Core", "Posts", "Analytics"], settings.EnabledFeatures);
        Assert.Equal(10, settings.ConfigurationData["Analytics:TopPostsCount"]);
    }

    [Fact(DisplayName = "Create preserves declaration order from object-map features")]
    public void Create_PreservesDeclarationOrder_FromObjectMapFeatures()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "OrderedShell",
            Features = [Feature("Zeta"), Feature("Alpha"), Feature("Mu")]
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal(["Zeta", "Alpha", "Mu"], settings.EnabledFeatures);
    }

    [Fact(DisplayName = "Create with inner Name property in object-map treats Name as configuration")]
    public void Create_WithInnerNameProperty_TreatsNameAsConfiguration()
    {
        // Arrange — in object-map syntax, the map key "Analytics" is the identity;
        //           an inner "Name" property is just feature configuration
        var config = new ShellConfig
        {
            Name = "InnerNameShell",
            Features =
            [
                new FeatureEntry
                {
                    Name = "Analytics",
                    Settings = new() { ["Name"] = "AnalyticsDisplay", ["TopPostsCount"] = 5 }
                }
            ]
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert
        Assert.Equal(["Analytics"], settings.EnabledFeatures);
        Assert.Equal("AnalyticsDisplay", settings.ConfigurationData["Analytics:Name"]);
        Assert.Equal(5, settings.ConfigurationData["Analytics:TopPostsCount"]);
    }

    [Fact(DisplayName = "ValidateNoDuplicateFeatures throws for duplicate feature names")]
    public void ValidateNoDuplicateFeatures_ThrowsForDuplicates()
    {
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigurationHelper.ValidateNoDuplicateFeatures(FeatureList("Core", "Analytics", "Core"), "TestShell"));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core", ex.Message);
        Assert.Contains("TestShell", ex.Message);
    }

    [Fact(DisplayName = "ValidateNoDuplicateFeatures succeeds for unique feature names")]
    public void ValidateNoDuplicateFeatures_SucceedsForUniqueNames()
    {
        // Act & Assert — should not throw
        ConfigurationHelper.ValidateNoDuplicateFeatures(FeatureList("Core", "Analytics", "Posts"), "TestShell");
    }

    [Fact(DisplayName = "ValidateNoDuplicateFeatures reports all duplicate names")]
    public void ValidateNoDuplicateFeatures_ReportsAllDuplicateNames()
    {
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigurationHelper.ValidateNoDuplicateFeatures(FeatureList("Core", "Analytics", "Core", "Analytics"), "TestShell"));
        Assert.Contains("Core", ex.Message);
        Assert.Contains("Analytics", ex.Message);
    }

    [Fact(DisplayName = "Create with duplicate features throws with shell name context")]
    public void Create_WithDuplicateFeatures_ThrowsWithShellNameContext()
    {
        // Arrange
        var config = new ShellConfig
        {
            Name = "MyShell",
            Features = [Feature("Core"), Feature("Core")] // duplicate
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => ShellSettingsFactory.Create(config));
        Assert.Contains("MyShell", ex.Message);
        Assert.Contains("Core", ex.Message);
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Create with empty-named feature after whitespace trim filters it out")]
    public void Create_EmptyNamedFeature_IsFilteredOut()
    {
        // Arrange — a feature entry whose name becomes empty after trimming
        var config = new ShellConfig
        {
            Name = "TestShell",
            Features = [Feature("  "), Feature("Core")]
        };

        // Act
        var settings = ShellSettingsFactory.Create(config);

        // Assert — empty-name entry filtered out, only Core remains
        Assert.Equal(["Core"], settings.EnabledFeatures);
    }

    [Fact(DisplayName = "ValidateNoDuplicateFeatures catches whitespace-variant duplicates")]
    public void ValidateNoDuplicateFeatures_CatchesWhitespaceVariantDuplicates()
    {
        // Arrange — "Core" and " Core " differ only by whitespace but are effectively the same
        var entries = FeatureList("Core", " Core ");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigurationHelper.ValidateNoDuplicateFeatures(entries, "TestShell"));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core", ex.Message);
        Assert.Contains("TestShell", ex.Message);
    }
}
