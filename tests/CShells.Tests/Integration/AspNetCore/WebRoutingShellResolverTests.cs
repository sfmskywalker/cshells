using CShells.AspNetCore.Resolution;
using CShells.Lifecycle;
using CShells.Resolution;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Tests for <see cref="WebRoutingShellResolver"/> — the resolver that inspects each active
/// shell's <c>WebRouting:*</c> config keys to identify the target shell.
/// </summary>
public class WebRoutingShellResolverTests
{
    private const string Tenant1 = "Tenant1";
    private const string Tenant2 = "Tenant2";
    private const string HeaderName = "X-Tenant-Id";
    private const string ClaimKey = "tenant_id";

    // ---------- Path routing ----------

    [Fact(DisplayName = "Resolves shell by path prefix")]
    public void Path_ResolvesShell()
    {
        var registry = new FakeRegistry()
            .WithShell(Tenant1, cd => cd["WebRouting:Path"] = "tenant1")
            .WithShell(Tenant2, cd => cd["WebRouting:Path"] = "tenant2");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Equal(new ShellId(Tenant1), resolver.Resolve(CreateContext(path: "/tenant1/api")));
    }

    [Fact(DisplayName = "Path matching is case-insensitive")]
    public void Path_CaseInsensitive()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:Path"] = "tenant1");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Equal(new ShellId(Tenant1), resolver.Resolve(CreateContext(path: "/TENANT1/api")));
    }

    [Fact(DisplayName = "Returns null when no path matches")]
    public void Path_NoMatch_ReturnsNull()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:Path"] = "tenant1");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Null(resolver.Resolve(CreateContext(path: "/api/users")));
    }

    [Fact(DisplayName = "Path routing can be disabled")]
    public void Path_CanBeDisabled()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:Path"] = "tenant1");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions { EnablePathRouting = false });

        Assert.Null(resolver.Resolve(CreateContext(path: "/tenant1/api")));
    }

    [Fact(DisplayName = "ExcludePaths prevents matching")]
    public void Path_ExcludePaths_Skipped()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:Path"] = "tenant1");
        var resolver = new WebRoutingShellResolver(
            registry,
            new WebRoutingShellResolverOptions { ExcludePaths = ["/tenant1"] });

        Assert.Null(resolver.Resolve(CreateContext(path: "/tenant1/api")));
    }

    [Fact(DisplayName = "Path starting with slash throws (config error)")]
    public void Path_InvalidConfig_Throws()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:Path"] = "/tenant1");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(CreateContext(path: "/tenant1/api")));
    }

    // ---------- Host routing ----------

    [Fact(DisplayName = "Resolves shell by host")]
    public void Host_ResolvesShell()
    {
        var registry = new FakeRegistry()
            .WithShell(Tenant1, cd => cd["WebRouting:Host"] = "tenant1.example.com");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Equal(new ShellId(Tenant1), resolver.Resolve(CreateContext(host: "tenant1.example.com")));
    }

    [Fact(DisplayName = "Host matching is case-insensitive")]
    public void Host_CaseInsensitive()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:Host"] = "tenant1.example.com");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Equal(new ShellId(Tenant1), resolver.Resolve(CreateContext(host: "TENANT1.EXAMPLE.COM")));
    }

    [Fact(DisplayName = "Host routing can be disabled")]
    public void Host_CanBeDisabled()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:Host"] = "tenant1.example.com");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions { EnableHostRouting = false });

        Assert.Null(resolver.Resolve(CreateContext(host: "tenant1.example.com")));
    }

    // ---------- Header routing ----------

    [Fact(DisplayName = "Resolves shell by header identifier")]
    public void Header_ResolvesByIdentifier()
    {
        var registry = new FakeRegistry()
            .WithShell(Tenant1, cd => cd["WebRouting:HeaderName"] = HeaderName);
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions { HeaderName = HeaderName });
        var context = new ShellResolutionContext();
        context.Set($"Header:{HeaderName}", Tenant1);

        Assert.Equal(new ShellId(Tenant1), resolver.Resolve(context));
    }

    [Fact(DisplayName = "Header routing requires options.HeaderName")]
    public void Header_NoOption_ReturnsNull()
    {
        var registry = new FakeRegistry().WithShell(Tenant1, cd => cd["WebRouting:HeaderName"] = HeaderName);
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions { HeaderName = null });
        var context = new ShellResolutionContext();
        context.Set($"Header:{HeaderName}", Tenant1);

        Assert.Null(resolver.Resolve(context));
    }

    // ---------- Claim routing ----------

    [Fact(DisplayName = "Resolves shell by claim identifier")]
    public void Claim_ResolvesByIdentifier()
    {
        var registry = new FakeRegistry()
            .WithShell(Tenant1, cd => cd["WebRouting:ClaimKey"] = ClaimKey);
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions { ClaimKey = ClaimKey });
        var context = new ShellResolutionContext();
        context.Set($"Claim:{ClaimKey}", Tenant1);

        Assert.Equal(new ShellId(Tenant1), resolver.Resolve(context));
    }

    // ---------- Root path ----------

    [Fact(DisplayName = "Root-path shell (WebRouting:Path = \"\") handles unmatched requests")]
    public void RootPath_FallbackShell()
    {
        var registry = new FakeRegistry()
            .WithShell(Tenant1, cd => cd["WebRouting:Path"] = "tenant1")
            .WithShell("Default", cd => cd["WebRouting:Path"] = "");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Equal(new ShellId("Default"), resolver.Resolve(CreateContext(path: "/unknown")));
        Assert.Equal(new ShellId("Default"), resolver.Resolve(CreateContext(path: "/")));
    }

    [Fact(DisplayName = "Ambiguous root-path (multiple shells with empty path) returns null")]
    public void RootPath_Ambiguous_ReturnsNull()
    {
        var registry = new FakeRegistry()
            .WithShell("A", cd => cd["WebRouting:Path"] = "")
            .WithShell("B", cd => cd["WebRouting:Path"] = "");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        Assert.Null(resolver.Resolve(CreateContext(path: "/unknown")));
    }

    [Fact(DisplayName = "Priority: path > host > header > claim > root")]
    public void Priority_PathWinsOverHost()
    {
        var registry = new FakeRegistry()
            .WithShell(Tenant1, cd => cd["WebRouting:Path"] = "tenant1")
            .WithShell(Tenant2, cd => cd["WebRouting:Host"] = "tenant2.example.com");
        var resolver = new WebRoutingShellResolver(registry, new WebRoutingShellResolverOptions());

        var context = CreateContext(path: "/tenant1/api", host: "tenant2.example.com");
        Assert.Equal(new ShellId(Tenant1), resolver.Resolve(context));
    }

    // =================================================================
    // Test doubles
    // =================================================================

    private static ShellResolutionContext CreateContext(string? path = null, string? host = null)
    {
        var ctx = new ShellResolutionContext();
        if (path is not null) ctx.Set(ShellResolutionContextKeys.Path, path);
        if (host is not null) ctx.Set(ShellResolutionContextKeys.Host, host);
        return ctx;
    }

    private sealed class FakeRegistry : IShellRegistry
    {
        private readonly Dictionary<string, IShell> _shells = new(StringComparer.OrdinalIgnoreCase);

        public FakeRegistry WithShell(string name, Action<IDictionary<string, object>> configureData)
        {
            var settings = new ShellSettings(new ShellId(name));
            configureData(settings.ConfigurationData);
            var services = new ServiceCollection();
            services.AddSingleton(settings);
            var provider = services.BuildServiceProvider();
            _shells[name] = new FakeShell(ShellDescriptor.Create(name, 1), provider);
            return this;
        }

        public void RegisterBlueprint(IShellBlueprint blueprint) => throw new NotSupportedException();
        public IShellBlueprint? GetBlueprint(string name) => null;
        public IReadOnlyCollection<string> GetBlueprintNames() => _shells.Keys.ToList();
        public Task<IShell> ActivateAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken ct = default) => throw new NotSupportedException();
        public IShell? GetActive(string name) => _shells.TryGetValue(name, out var s) ? s : null;
        public IReadOnlyCollection<IShell> GetAll(string name) => _shells.TryGetValue(name, out var s) ? [s] : [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }

    private sealed class FakeShell(ShellDescriptor descriptor, IServiceProvider provider) : IShell
    {
        public ShellDescriptor Descriptor { get; } = descriptor;
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider { get; } = provider;
        public IShellScope BeginScope() => throw new NotSupportedException();
    }
}
