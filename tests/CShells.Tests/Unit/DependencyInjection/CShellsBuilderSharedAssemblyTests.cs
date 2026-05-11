using CShells.DependencyInjection;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.DependencyInjection;

public class CShellsBuilderSharedAssemblyTests
{
    [Fact]
    public void WithSharedAssemblies_AppendsExactAndPrefixSelectors()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        builder.WithSharedAssemblies("Elsa", "Elsa.*");

        Assert.Equal(2, builder.SharedAssemblySelectors.Count);
    }

    [Fact]
    public void WithSharedAssemblies_WithNullPatterns_ThrowsArgumentNullException()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.WithSharedAssemblies(patterns: null!));

        Assert.Equal("patterns", exception.ParamName);
    }

    [Fact]
    public void WithSharedAssembliesWhere_AppendsPredicateSelector()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        builder.WithSharedAssembliesWhere(name => name.StartsWith("Elsa.", StringComparison.OrdinalIgnoreCase));

        var selector = Assert.Single(builder.SharedAssemblySelectors);
        Assert.True(selector.TryMatch("Elsa.Workflows", out var match));
        Assert.NotNull(match);
        Assert.Equal(SharedAssemblySelectorKind.Predicate, match.SelectorKind);
    }

    [Fact]
    public void WithSharedAssembliesWhere_WithNullPredicate_ThrowsArgumentNullException()
    {
        var builder = new CShellsBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.WithSharedAssembliesWhere(null!));

        Assert.Equal("predicate", exception.ParamName);
    }

    [Fact]
    public void PredicateSelector_WhenPredicateThrows_WrapsSourceFeedback()
    {
        var selector = SharedAssemblySelector.FromPredicate(_ => throw new InvalidOperationException("boom"), "WithSharedAssembliesWhere");

        var exception = Assert.Throws<InvalidOperationException>(() => selector.TryMatch("Elsa.Workflows", out _));

        Assert.Contains("WithSharedAssembliesWhere", exception.Message);
        Assert.Contains("Elsa.Workflows", exception.Message);
        Assert.NotNull(exception.InnerException);
    }
}
