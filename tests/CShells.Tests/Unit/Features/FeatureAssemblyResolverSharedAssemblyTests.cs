using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Features;

public class FeatureAssemblyResolverSharedAssemblyTests
{
    [Fact]
    public void SharedAssemblySelectorProvider_WithConfiguredSelectors_MatchesExpectedSimpleNames()
    {
        var provider = CreateProvider("Elsa", "Elsa.*");

        var matches =
            new[]
            {
                new AssemblyName("Elsa"),
                new AssemblyName("Elsa.Workflows"),
                new AssemblyName("Contoso.Workflows")
            }
            .Where(provider.IsMatch)
            .Select(name => name.Name!)
            .ToArray();

        Assert.Equal(["Elsa", "Elsa.Workflows"], matches);
    }

    [Fact]
    public async Task BuildFeatureAssembliesAsync_WithConfigurationAndCodeFirst_DeduplicatesMatches()
    {
        var services = new ServiceCollection();
        var builder = new CShellsBuilder(services);
        var assembly = typeof(FeatureAssemblyResolverSharedAssemblyTests).Assembly;
        using var serviceProvider = services.BuildServiceProvider();

        builder.WithAssemblies(assembly);
        builder.WithSharedAssemblies(assembly.GetName().Name!);

        var assemblies = await builder.BuildFeatureAssembliesAsync(serviceProvider);

        Assert.Equal(assembly, Assert.Single(assemblies, item => item == assembly));
    }

    [Fact]
    public void SharedAssemblySelectorProvider_UsesAssemblySimpleNameOnly()
    {
        var provider = CreateProvider("Elsa.*");
        var assemblyName = new AssemblyName("Elsa.Workflows")
        {
            Version = new(1, 2, 3, 4),
            CultureName = "en-US"
        };

        Assert.True(provider.IsMatch(assemblyName));
        Assert.False(CreateProvider("1.2.3.*").IsMatch(assemblyName));
        Assert.False(CreateProvider("plugins/Elsa.*").IsMatch(assemblyName));
    }

    [Fact]
    public void SharedAssemblySelectorProvider_DeduplicatesMatchDiagnosticsBySimpleName()
    {
        var provider = CreateProvider("Elsa.*", "Elsa.Workflows");

        Assert.True(provider.IsMatch(new("Elsa.Workflows")));
        Assert.True(provider.IsMatch(new("elsa.workflows")));

        var match = Assert.Single(provider.Matches);
        Assert.Equal("Elsa.Workflows", match.AssemblyName);
        Assert.Equal("CShells:SharedAssemblies:0", match.SelectorSource);
    }

    [Fact]
    public async Task BuildFeatureAssembliesAsync_WithPredicateSelector_FiltersHostAssemblies()
    {
        var services = new ServiceCollection();
        var builder = new CShellsBuilder(services);
        var assemblyName = typeof(CShellsBuilder).Assembly.GetName().Name!;
        using var serviceProvider = services.BuildServiceProvider();

        builder.WithSharedAssembliesWhere(name => name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));

        var assemblies = await builder.BuildFeatureAssembliesAsync(serviceProvider);

        Assert.Contains(assemblies, assembly => assembly.GetName().Name == assemblyName);
        Assert.Contains(builder.SharedAssemblyMatches, match => match.AssemblyName == assemblyName);
    }

    private static SharedAssemblySelectorProvider CreateProvider(params string[] patterns) =>
        new(patterns.Select((pattern, index) => SharedAssemblySelector.FromPattern(pattern, $"CShells:SharedAssemblies:{index}")));
}
