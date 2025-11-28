namespace CShells.Tests;

public class ShellFeatureAttributeTests
{
    private const string TestFeatureName = "TestFeature";

    [Fact]
    public void Constructor_WithValidName_SetsName()
    {
        // Act
        var attribute = new ShellFeatureAttribute(TestFeatureName);

        // Assert
        Assert.Equal(TestFeatureName, attribute.Name);
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ShellFeatureAttribute(null!));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void DependsOn_DefaultsToEmptyArray()
    {
        // Act
        var attribute = new ShellFeatureAttribute(TestFeatureName);

        // Assert
        Assert.NotNull(attribute.DependsOn);
        Assert.Empty(attribute.DependsOn);
    }

    [Fact]
    public void DependsOn_CanBeSet()
    {
        // Arrange
        var attribute = new ShellFeatureAttribute(TestFeatureName);
        var dependencies = new[] { "Feature1", "Feature2" };

        // Act
        attribute.DependsOn = dependencies;

        // Assert
        Assert.Equal(dependencies, attribute.DependsOn);
    }

    [Fact]
    public void Metadata_DefaultsToEmptyArray()
    {
        // Act
        var attribute = new ShellFeatureAttribute(TestFeatureName);

        // Assert
        Assert.NotNull(attribute.Metadata);
        Assert.Empty(attribute.Metadata);
    }

    [Fact]
    public void Metadata_CanBeSet()
    {
        // Arrange
        var attribute = new ShellFeatureAttribute(TestFeatureName);
        var metadata = new object[] { "key1", "value1", "key2", 42 };

        // Act
        attribute.Metadata = metadata;

        // Assert
        Assert.Equal(metadata, attribute.Metadata);
    }

    [Fact]
    public void Attribute_CanBeAppliedToClass()
    {
        // Arrange & Act
        var attribute = typeof(TestFeatureClass).GetCustomAttributes(typeof(ShellFeatureAttribute), false)
            .Cast<ShellFeatureAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal("TestFeature", attribute.Name);
        Assert.Equal(["DependencyFeature"], attribute.DependsOn);
    }

    [ShellFeature("TestFeature", DependsOn = ["DependencyFeature"])]
    private class TestFeatureClass
    {
    }
}
