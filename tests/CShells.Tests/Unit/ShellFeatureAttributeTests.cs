using CShells.Features;

namespace CShells.Tests.Unit;

public class ShellFeatureAttributeTests
{
    [Fact(DisplayName = "Constructor with explicit name sets Name property")]
    public void Constructor_WithExplicitName_SetsNameProperty()
    {
        // Arrange & Act
        var attribute = new ShellFeatureAttribute("TestFeature");

        // Assert
        Assert.Equal("TestFeature", attribute.Name);
    }

    [Fact(DisplayName = "Constructor without name sets Name to null")]
    public void Constructor_WithoutName_SetsNameToNull()
    {
        // Arrange & Act
        var attribute = new ShellFeatureAttribute();

        // Assert
        Assert.Null(attribute.Name);
    }

    [Fact(DisplayName = "Constructor with null name sets Name to null")]
    public void Constructor_WithNullName_SetsNameToNull()
    {
        // Arrange & Act
        var attribute = new ShellFeatureAttribute(null);

        // Assert
        Assert.Null(attribute.Name);
    }
}
