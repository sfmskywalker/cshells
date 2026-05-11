using CShells.Configuration;
using CShells.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Configuration;

public class SharedAssemblyConfigurationTests
{
    [Fact]
    public void CShellsOptions_BindsRootSharedAssembliesCollection()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CShells:SharedAssemblies:0"] = "Elsa",
            ["CShells:SharedAssemblies:1"] = "Elsa.*"
        });

        var options = configuration.GetSection("CShells").Get<CShellsOptions>();

        Assert.NotNull(options);
        Assert.Equal(["Elsa", "Elsa.*"], options.SharedAssemblies);
    }

    [Fact]
    public void WithConfigurationProvider_LoadsRootSharedAssembliesAsSelectors()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CShells:SharedAssemblies:0"] = "Elsa",
            ["CShells:SharedAssemblies:1"] = "Elsa.*"
        });

        builder.WithConfigurationProvider(configuration);

        Assert.Equal(2, builder.SharedAssemblySelectors.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithConfigurationProvider_WithBlankSharedAssembly_ThrowsConfigurationPath(string pattern)
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CShells:SharedAssemblies:0"] = pattern
        });

        var exception = Assert.Throws<ArgumentException>(() => builder.WithConfigurationProvider(configuration));

        Assert.Contains("CShells:SharedAssemblies:0", exception.Message);
    }

    [Fact]
    public void WithConfigurationProvider_WithInvalidWildcard_ThrowsConfigurationPath()
    {
        var builder = new CShellsBuilder(new ServiceCollection());
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CShells:SharedAssemblies:0"] = "*.Contracts"
        });

        var exception = Assert.Throws<ArgumentException>(() => builder.WithConfigurationProvider(configuration));

        Assert.Contains("CShells:SharedAssemblies:0", exception.Message);
        Assert.Contains("final character", exception.Message);
    }

    [Fact]
    public void DocumentationSamples_UseRootSharedAssembliesCollection()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        var quickstart = File.ReadAllText(Path.Combine(RepositoryRoot, "specs", "012-pattern-shared-assemblies", "quickstart.md"));

        Assert.Contains("CShells:SharedAssemblies", quickstart);
        Assert.DoesNotContain("CShells:Shells:<Name>:SharedAssemblies", readme);
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CShells.sln")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
        }
    }
}
