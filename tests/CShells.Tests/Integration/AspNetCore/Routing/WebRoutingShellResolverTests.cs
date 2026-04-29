using CShells.AspNetCore.Resolution;
using CShells.AspNetCore.Routing;
using CShells.Resolution;
using CShells.Tests.TestHelpers;

namespace CShells.Tests.Integration.AspNetCore.Routing;

/// <summary>
/// End-to-end integration tests for <see cref="WebRoutingShellResolver"/> using a real
/// <see cref="DefaultShellRouteIndex"/> backed by <see cref="StubShellBlueprintProvider"/>.
/// These cover the headline US1 / US2 regressions: a cold blueprint resolves and a reloaded
/// (drained) blueprint re-activates on the next matching request.
/// </summary>
public class WebRoutingShellResolverTests
{
    [Fact(DisplayName = "Cold path-by-name request resolves to blueprint without prior activation")]
    public async Task ColdRequest_PathByName_Resolves()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var resolver = BuildResolver(provider);

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/acme/posts");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.NotNull(shellId);
        Assert.Equal("acme", shellId!.Value.Name);
    }

    [Fact(DisplayName = "Cold root-path request resolves to root-eligible blueprint")]
    public async Task ColdRequest_RootPath_Resolves()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""));

        var resolver = BuildResolver(provider);

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.NotNull(shellId);
        Assert.Equal("Default", shellId!.Value.Name);
    }

    [Fact(DisplayName = "Path-by-name miss falls back to root-eligible blueprint")]
    public async Task PathByNameMiss_FallsBackToRootPath()
    {
        // Regression: "/elsa/api/identity/login" against a deployment whose only blueprint
        // declares WebRouting:Path = "" must resolve to that root-path blueprint, mirroring
        // the legacy resolver's path > host > header > claim > root priority. Otherwise the
        // cold-start 404 the route index is meant to fix reappears for non-root paths.
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""));

        var resolver = BuildResolver(provider);

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/elsa/api/identity/login");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.NotNull(shellId);
        Assert.Equal("Default", shellId!.Value.Name);
    }

    [Fact(DisplayName = "Path-by-name match runs without enumerating the catalogue (preserves 100k scaling)")]
    public async Task PathByName_DoesNotCallListAsync()
    {
        var provider = new StubShellBlueprintProvider();
        for (var i = 0; i < 100; i++)
        {
            var name = $"tenant{i:000}";
            provider.Add(name, b => b.WithConfiguration("WebRouting:Path", name));
        }

        var resolver = BuildResolver(provider);

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/tenant042/api");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.NotNull(shellId);
        Assert.Equal("tenant042", shellId!.Value.Name);
        Assert.Equal(0, provider.ListCount);
    }

    [Fact(DisplayName = "Unmatched request returns null (not an exception)")]
    public async Task NoMatch_ReturnsNull()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var resolver = BuildResolver(provider);

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/unknown/x");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.Null(shellId);
    }

    [Fact(DisplayName = "Path routing disabled short-circuits without consulting provider")]
    public async Task PathRoutingDisabled_DoesNotCallProvider()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var resolver = BuildResolver(provider, opt =>
        {
            opt.EnablePathRouting = false;
            opt.EnableHostRouting = false;
        });

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/acme/x");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.Null(shellId);
        Assert.Equal(0, provider.LookupCount);
    }

    [Fact(DisplayName = "Excluded path does not consult provider")]
    public async Task ExcludedPath_DoesNotCallProvider()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var resolver = BuildResolver(provider, opt => opt.ExcludePaths = ["/health"]);

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/health/check");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.Null(shellId);
        Assert.Equal(0, provider.LookupCount);
    }

    [Fact(DisplayName = "Provider unavailable on initial population swallows exception and returns null")]
    public async Task ProviderUnavailable_RootPath_ReturnsNull_NoThrow()
    {
        var provider = new StubShellBlueprintProvider();
        provider.ThrowOnList = new InvalidOperationException("DB unreachable");

        var resolver = BuildResolver(provider);

        var ctx = new ShellResolutionContext();
        ctx.Set(ShellResolutionContextKeys.Path, "/");

        var shellId = await resolver.ResolveAsync(ctx);

        Assert.Null(shellId);
    }

    private static WebRoutingShellResolver BuildResolver(
        StubShellBlueprintProvider provider,
        Action<WebRoutingShellResolverOptions>? configureOptions = null)
    {
        var options = new WebRoutingShellResolverOptions();
        configureOptions?.Invoke(options);

        var index = new DefaultShellRouteIndex(provider);
        return new WebRoutingShellResolver(index, options);
    }
}
