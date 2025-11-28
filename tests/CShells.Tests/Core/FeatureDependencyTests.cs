using FluentAssertions;

namespace CShells.Tests.Core;

/// <summary>
/// Tests for the <see cref="FeatureDependencyResolver"/> class covering transitive dependencies,
/// cycle detection, and handling of unknown feature dependencies.
/// </summary>
public class FeatureDependencyTests
{
    private readonly FeatureDependencyResolver _resolver = new();

    #region Transitive Dependency Resolution

    [Fact]
    public void GetOrderedFeatures_WithTransitiveDependencies_ReturnsTopologicalOrder()
    {
        // Arrange: A -> B -> C (A depends on B, B depends on C)
        var features = CreateFeatureDictionary(
            ("A", new[] { "B" }),
            ("B", new[] { "C" }),
            ("C", Array.Empty<string>())
        );

        // Act
        var result = _resolver.GetOrderedFeatures(new[] { "A" }, features);

        // Assert: Dependencies should come before dependents [C, B, A]
        result.Should().HaveCount(3);
        result.Should().ContainInOrder("C", "B", "A");
    }

    [Fact]
    public void GetOrderedFeatures_WithDeepTransitiveDependencies_ReturnsDependenciesBeforeDependents()
    {
        // Arrange: A -> B -> C -> D -> E
        var features = CreateFeatureDictionary(
            ("A", new[] { "B" }),
            ("B", new[] { "C" }),
            ("C", new[] { "D" }),
            ("D", new[] { "E" }),
            ("E", Array.Empty<string>())
        );

        // Act
        var result = _resolver.GetOrderedFeatures(new[] { "A" }, features);

        // Assert
        result.Should().HaveCount(5);
        
        // Each feature should come after its dependencies
        result.IndexOf("E").Should().BeLessThan(result.IndexOf("D"));
        result.IndexOf("D").Should().BeLessThan(result.IndexOf("C"));
        result.IndexOf("C").Should().BeLessThan(result.IndexOf("B"));
        result.IndexOf("B").Should().BeLessThan(result.IndexOf("A"));
    }

    [Fact]
    public void GetOrderedFeatures_WithMultipleDependencies_ReturnsDependenciesFirst()
    {
        // Arrange: A depends on both B and C, which have no dependencies
        var features = CreateFeatureDictionary(
            ("A", new[] { "B", "C" }),
            ("B", Array.Empty<string>()),
            ("C", Array.Empty<string>())
        );

        // Act
        var result = _resolver.GetOrderedFeatures(new[] { "A" }, features);

        // Assert
        result.Should().HaveCount(3);
        result.IndexOf("B").Should().BeLessThan(result.IndexOf("A"));
        result.IndexOf("C").Should().BeLessThan(result.IndexOf("A"));
    }

    [Fact]
    public void GetOrderedFeatures_WithDiamondDependency_HandlesDuplicatesCorrectly()
    {
        // Arrange: Diamond pattern A -> B, A -> C, B -> D, C -> D
        var features = CreateFeatureDictionary(
            ("A", new[] { "B", "C" }),
            ("B", new[] { "D" }),
            ("C", new[] { "D" }),
            ("D", Array.Empty<string>())
        );

        // Act
        var result = _resolver.GetOrderedFeatures(new[] { "A" }, features);

        // Assert: D should appear only once and before B and C
        result.Should().HaveCount(4);
        result.Should().ContainSingle(f => f == "D");
        result.IndexOf("D").Should().BeLessThan(result.IndexOf("B"));
        result.IndexOf("D").Should().BeLessThan(result.IndexOf("C"));
    }

    #endregion

    #region Cycle Detection

    [Fact]
    public void GetOrderedFeatures_WithDirectCycle_ThrowsInvalidOperationException()
    {
        // Arrange: A depends on B, B depends on A (direct cycle)
        var features = CreateFeatureDictionary(
            ("A", new[] { "B" }),
            ("B", new[] { "A" })
        );

        // Act & Assert
        var act = () => _resolver.GetOrderedFeatures(new[] { "A" }, features);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void GetOrderedFeatures_WithIndirectCycle_ThrowsInvalidOperationException()
    {
        // Arrange: A -> B -> C -> A (indirect cycle)
        var features = CreateFeatureDictionary(
            ("A", new[] { "B" }),
            ("B", new[] { "C" }),
            ("C", new[] { "A" })
        );

        // Act & Assert
        var act = () => _resolver.GetOrderedFeatures(new[] { "A" }, features);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void GetOrderedFeatures_WithSelfReferentialDependency_ThrowsInvalidOperationException()
    {
        // Arrange: A depends on itself
        var features = CreateFeatureDictionary(
            ("A", new[] { "A" })
        );

        // Act & Assert
        var act = () => _resolver.GetOrderedFeatures(new[] { "A" }, features);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void ResolveDependencies_WithCycle_ThrowsInvalidOperationExceptionWithFeatureName()
    {
        // Arrange
        var features = CreateFeatureDictionary(
            ("A", new[] { "B" }),
            ("B", new[] { "A" })
        );

        // Act & Assert
        var act = () => _resolver.ResolveDependencies("A", features);
        var exception = act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*")
            .Which;
        (exception.Message.Contains("A") || exception.Message.Contains("B")).Should().BeTrue(
            "exception message should contain the feature name involved in the cycle");
    }

    #endregion

    #region Unknown Feature Dependency Handling

    [Fact]
    public void GetOrderedFeatures_WithUnknownDependency_ThrowsInvalidOperationException()
    {
        // Arrange: A depends on "NonExistent" which is not in the features dictionary
        var features = CreateFeatureDictionary(
            ("A", new[] { "NonExistent" })
        );

        // Act & Assert
        var act = () => _resolver.GetOrderedFeatures(new[] { "A" }, features);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void GetOrderedFeatures_WithUnknownDependency_MessageContainsFeatureName()
    {
        // Arrange
        var features = CreateFeatureDictionary(
            ("A", new[] { "MissingFeature" })
        );

        // Act & Assert
        var act = () => _resolver.GetOrderedFeatures(new[] { "A" }, features);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MissingFeature*");
    }

    [Fact]
    public void ResolveDependencies_WithUnknownFeature_ThrowsInvalidOperationException()
    {
        // Arrange
        var features = CreateFeatureDictionary(
            ("A", Array.Empty<string>())
        );

        // Act & Assert
        var act = () => _resolver.ResolveDependencies("NonExistentFeature", features);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NonExistentFeature*")
            .WithMessage("*not found*");
    }

    [Fact]
    public void GetOrderedFeatures_WithUnknownTransitiveDependency_ThrowsInvalidOperationException()
    {
        // Arrange: A -> B -> NonExistent
        var features = CreateFeatureDictionary(
            ("A", new[] { "B" }),
            ("B", new[] { "NonExistent" })
        );

        // Act & Assert
        var act = () => _resolver.GetOrderedFeatures(new[] { "A" }, features);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NonExistent*")
            .WithMessage("*not found*");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a minimal feature dictionary for testing dependency resolution.
    /// Only populates the Id and Dependencies properties since those are what
    /// the FeatureDependencyResolver operates on.
    /// </summary>
    private static Dictionary<string, ShellFeatureDescriptor> CreateFeatureDictionary(
        params (string Name, string[] Dependencies)[] features)
    {
        var dict = new Dictionary<string, ShellFeatureDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, dependencies) in features)
        {
            dict[name] = new ShellFeatureDescriptor(name) { Dependencies = dependencies };
        }
        return dict;
    }

    #endregion
}
