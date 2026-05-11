using CShells.Features;

namespace CShells.Tests.Unit.Features;

public class SharedAssemblyPatternTests
{
    [Theory]
    [InlineData("Elsa", "Elsa", true)]
    [InlineData("Elsa", "Elsa.Workflows", false)]
    [InlineData("Elsa.*", "Elsa.Workflows", true)]
    [InlineData("Elsa.*", "Contoso.Workflows", false)]
    [InlineData("elsa.*", "Elsa.Workflows", true)]
    public void IsMatch_WithExactAndPrefixPatterns_MatchesExpectedSimpleNames(string pattern, string assemblyName, bool expected)
    {
        var selector = SharedAssemblyPattern.Parse(pattern, "test");

        var actual = selector.IsMatch(assemblyName);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("*.Contracts")]
    [InlineData("Elsa.*.Abstractions")]
    [InlineData("Elsa**")]
    public void Parse_WithNonFinalWildcard_ThrowsArgumentException(string pattern)
    {
        var exception = Assert.Throws<ArgumentException>(() => SharedAssemblyPattern.Parse(pattern, "CShells:SharedAssemblies:0"));

        Assert.Contains("final character", exception.Message);
        Assert.Contains("CShells:SharedAssemblies:0", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithMissingPattern_ThrowsActionableException(string? pattern)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => SharedAssemblyPattern.Parse(pattern, "CShells:SharedAssemblies:0"));

        Assert.Contains("CShells:SharedAssemblies:0", exception.Message);
    }

    [Fact]
    public void Parse_WithBareWildcard_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => SharedAssemblyPattern.Parse("*", "CShells:SharedAssemblies:0"));

        Assert.Contains("non-empty prefix", exception.Message);
    }
}
