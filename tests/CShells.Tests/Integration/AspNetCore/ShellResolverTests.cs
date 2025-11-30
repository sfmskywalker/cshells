using CShells.AspNetCore;
using CShells.AspNetCore.Resolution;
using CShells.Resolution;
using Microsoft.AspNetCore.Http;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Consolidated tests for all <see cref="IShellResolver"/> implementations.
/// </summary>
public class ShellResolverTests
{
    private const string Tenant1Host = "tenant1.example.com";
    private const string Tenant2Host = "tenant2.example.com";
    private const string Tenant1Path = "tenant1";
    private const string Tenant2Path = "tenant2";
    private const string Localhost = "localhost";

    public static TheoryData<IShellResolver, ShellResolutionContext, ShellId?, string> ResolverTestCases => new()
    {
        // HostShellResolver - matching hosts
        { CreateHostResolver(), CreateResolutionContext(Tenant1Host), new ShellId("Tenant1Shell"), "HostResolver with matching host" },
        { CreateHostResolver(), CreateResolutionContext(Tenant2Host), new ShellId("Tenant2Shell"), "HostResolver with second matching host" },
        { CreateHostResolver(), CreateResolutionContext(Localhost), new ShellId("LocalhostShell"), "HostResolver with localhost" },
        { CreateHostResolver(), CreateResolutionContext("TENANT1.EXAMPLE.COM"), new ShellId("Tenant1Shell"), "HostResolver case-insensitive host" },

        // HostShellResolver - non-matching
        { CreateHostResolver(), CreateResolutionContext("unknown.example.com"), null, "HostResolver with non-matching host" },

        // PathShellResolver - matching paths
        { CreatePathResolver(), CreateResolutionContext(path: $"/{Tenant1Path}/some/path"), new ShellId("Tenant1Shell"), "PathResolver with matching first segment" },
        { CreatePathResolver(), CreateResolutionContext(path: $"/{Tenant2Path}/api"), new ShellId("Tenant2Shell"), "PathResolver with matching second segment" },
        { CreatePathResolver(), CreateResolutionContext(path: $"/{Tenant1Path}"), new ShellId("Tenant1Shell"), "PathResolver with single segment" },
        { CreatePathResolver(), CreateResolutionContext(path: "/TENANT1/path"), new ShellId("Tenant1Shell"), "PathResolver case-insensitive path" },

        // PathShellResolver - non-matching
        { CreatePathResolver(), CreateResolutionContext(path: "/unknown/path"), null, "PathResolver with non-matching path" },
        { CreatePathResolver(), CreateResolutionContext(path: ""), null, "PathResolver with empty path" },
        { CreatePathResolver(), CreateResolutionContext(path: "/"), null, "PathResolver with root path" },
    };

    [Theory(DisplayName = "Resolve with various inputs returns expected result")]
    [MemberData(nameof(ResolverTestCases))]
    public void Resolve_WithVariousInputs_ReturnsExpectedResult(
        IShellResolver resolver,
        ShellResolutionContext context,
        ShellId? expectedShellId,
        string scenario)
    {
        // Act
        var result = resolver.Resolve(context);

        // Assert
        Assert.Equal(expectedShellId, result);
    }

    [Fact(DisplayName = "HostShellResolver constructor with null hostMap throws ArgumentNullException")]
    public void HostShellResolver_Constructor_WithNullHostMap_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new HostShellResolver(null!));
        Assert.Equal("hostMap", ex.ParamName);
    }

    [Fact(DisplayName = "PathShellResolver constructor with null pathMap throws ArgumentNullException")]
    public void PathShellResolver_Constructor_WithNullPathMap_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new PathShellResolver(null!));
        Assert.Equal("pathMap", ex.ParamName);
    }

    [Theory(DisplayName = "Resolve with null context throws ArgumentNullException")]
    [InlineData(typeof(HostShellResolver))]
    [InlineData(typeof(PathShellResolver))]
    public void Resolve_WithNullContext_ThrowsArgumentNullException(Type resolverType)
    {
        // Arrange
        var resolver = CreateResolverInstance(resolverType);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
        Assert.Equal("context", ex.ParamName);
    }

    #region Helper Methods

    private static HostShellResolver CreateHostResolver() => new(new Dictionary<string, ShellId>
    {
        [Tenant1Host] = new("Tenant1Shell"),
        [Tenant2Host] = new("Tenant2Shell"),
        [Localhost] = new("LocalhostShell")
    });

    private static PathShellResolver CreatePathResolver() => new(new Dictionary<string, ShellId>
    {
        [Tenant1Path] = new("Tenant1Shell"),
        [Tenant2Path] = new("Tenant2Shell")
    });

    private static IShellResolver CreateResolverInstance(Type resolverType)
    {
        var emptyMap = new Dictionary<string, ShellId>();
        return (IShellResolver)Activator.CreateInstance(resolverType, emptyMap)!;
    }

    private static ShellResolutionContext CreateResolutionContext(string? host = null, string? path = null)
    {
        var context = new ShellResolutionContext();

        if (host != null)
            context.Set(ShellResolutionContextKeys.Host, host);

        if (path != null)
            context.Set(ShellResolutionContextKeys.Path, path);

        return context;
    }

    #endregion
}
