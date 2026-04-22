using CShells.AspNetCore.Middleware;
using CShells.Lifecycle;
using CShells.Resolution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Tests for <see cref="ShellMiddleware"/> — the middleware that resolves a shell per request
/// and sets <see cref="HttpContext.RequestServices"/> to a scope from that shell's provider.
/// </summary>
public class ShellMiddlewareTests
{
    [Fact(DisplayName = "InvokeAsync with no shells registered continues without setting scope")]
    public async Task InvokeAsync_NoShellsRegistered_ContinuesWithoutSettingScope()
    {
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var nextCalled = false;

        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            registry: new FakeRegistry());

        var httpContext = new DefaultHttpContext { RequestServices = originalServiceProvider };

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
        Assert.Same(originalServiceProvider, httpContext.RequestServices);
    }

    [Fact(DisplayName = "InvokeAsync with null resolved ShellId continues without setting scope")]
    public async Task InvokeAsync_NullResolvedId_ContinuesWithoutSettingScope()
    {
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var shell = FakeShell.WithServices(_ => { });
        var registry = new FakeRegistry(shell);

        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            resolver: new NullShellResolver(),
            registry: registry);

        var httpContext = new DefaultHttpContext { RequestServices = originalServiceProvider };

        await middleware.InvokeAsync(httpContext);

        Assert.Same(originalServiceProvider, httpContext.RequestServices);
    }

    [Fact(DisplayName = "InvokeAsync with a resolved shell sets RequestServices to a shell scope")]
    public async Task InvokeAsync_ValidShell_SetsRequestServices_FromShellScope()
    {
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var shell = FakeShell.WithServices(s => s.AddSingleton<ITestService, TestService>(), name: "TestShell");

        IServiceProvider? capturedRequestServices = null;
        ITestService? capturedService = null;

        var middleware = CreateMiddleware(
            ctx =>
            {
                capturedRequestServices = ctx.RequestServices;
                capturedService = ctx.RequestServices.GetService<ITestService>();
                return Task.CompletedTask;
            },
            resolver: new FixedShellResolver("TestShell"),
            registry: new FakeRegistry(shell));

        var httpContext = new DefaultHttpContext { RequestServices = originalServiceProvider };

        await middleware.InvokeAsync(httpContext);

        Assert.NotNull(capturedRequestServices);
        Assert.NotSame(originalServiceProvider, capturedRequestServices);
        Assert.NotNull(capturedService);
        Assert.Equal(0, shell.ActiveScopeCount); // scope disposed after request
    }

    [Fact(DisplayName = "InvokeAsync increments the shell's scope counter for the request lifetime")]
    public async Task InvokeAsync_IncrementsScopeCounter_ForRequestLifetime()
    {
        var shell = FakeShell.WithServices(_ => { }, name: "TestShell");
        var registry = new FakeRegistry(shell);

        var middleware = CreateMiddleware(
            _ =>
            {
                Assert.Equal(1, shell.ActiveScopeCount);
                return Task.CompletedTask;
            },
            resolver: new FixedShellResolver("TestShell"),
            registry: registry);

        await middleware.InvokeAsync(new DefaultHttpContext());
        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Fact(DisplayName = "InvokeAsync disposes shell scope even after downstream exception")]
    public async Task InvokeAsync_DisposesScope_EvenAfterException()
    {
        var shell = FakeShell.WithServices(_ => { }, name: "TestShell");
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("boom"),
            resolver: new FixedShellResolver("TestShell"),
            registry: new FakeRegistry(shell));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext()));
        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Theory(DisplayName = "Constructor guard clauses throw ArgumentNullException")]
    [InlineData(true, false, false, false, false, "next")]
    [InlineData(false, true, false, false, false, "resolver")]
    [InlineData(false, false, true, false, false, "registry")]
    [InlineData(false, false, false, true, false, "cache")]
    [InlineData(false, false, false, false, true, "options")]
    public void Constructor_GuardClauses_ThrowArgumentNullException(
        bool nullNext, bool nullResolver, bool nullRegistry, bool nullCache, bool nullOptions, string expectedParam)
    {
        RequestDelegate? next = nullNext ? null : _ => Task.CompletedTask;
        IShellResolver? resolver = nullResolver ? null : new NullShellResolver();
        IShellRegistry? registry = nullRegistry ? null : new FakeRegistry();
        IMemoryCache? cache = nullCache ? null : new MemoryCache(new MemoryCacheOptions());
        IOptions<ShellMiddlewareOptions>? options = nullOptions ? null : Options.Create(new ShellMiddlewareOptions());

        var ex = Assert.Throws<ArgumentNullException>(() => new ShellMiddleware(next!, resolver!, registry!, cache!, options!));
        Assert.Equal(expectedParam, ex.ParamName);
    }

    // =================================================================
    // Test doubles
    // =================================================================

    private static ShellMiddleware CreateMiddleware(
        RequestDelegate next,
        IShellResolver? resolver = null,
        IShellRegistry? registry = null,
        IMemoryCache? cache = null,
        IOptions<ShellMiddlewareOptions>? options = null) =>
        new(
            next,
            resolver ?? new NullShellResolver(),
            registry ?? new FakeRegistry(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            options ?? Options.Create(new ShellMiddlewareOptions()));

    private interface ITestService;

    private sealed class TestService : ITestService;

    private sealed class NullShellResolver : IShellResolver
    {
        public ShellId? Resolve(ShellResolutionContext context) => null;
    }

    private sealed class FixedShellResolver(ShellId shellId) : IShellResolver
    {
        public ShellId? Resolve(ShellResolutionContext context) => shellId;
    }

    internal sealed class FakeRegistry(FakeShell? shell = null) : IShellRegistry
    {
        private readonly FakeShell? _shell = shell;

        public void RegisterBlueprint(IShellBlueprint blueprint) => throw new NotSupportedException();
        public IShellBlueprint? GetBlueprint(string name) => null;
        public IReadOnlyCollection<string> GetBlueprintNames() => _shell is null ? [] : [_shell.Descriptor.Name];
        public Task<IShell> ActivateAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken ct = default) => throw new NotSupportedException();
        public IShell? GetActive(string name)
            => _shell is not null && string.Equals(_shell.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase) ? _shell : null;
        public IReadOnlyCollection<IShell> GetAll(string name) => _shell is null ? [] : [_shell];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }

    internal sealed class FakeShell(ShellDescriptor descriptor, IServiceProvider provider) : IShell
    {
        private int _activeScopes;

        public ShellDescriptor Descriptor { get; } = descriptor;
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider { get; } = provider;
        public int ActiveScopeCount => Volatile.Read(ref _activeScopes);

        public IShellScope BeginScope()
        {
            Interlocked.Increment(ref _activeScopes);
            var scope = ServiceProvider.CreateAsyncScope();
            return new FakeScope(this, scope);
        }

        public static FakeShell WithServices(Action<IServiceCollection> configure, string name = "TestShell")
        {
            var services = new ServiceCollection();
            configure(services);
            return new FakeShell(ShellDescriptor.Create(name, 1), services.BuildServiceProvider());
        }

        private sealed class FakeScope(FakeShell owner, AsyncServiceScope inner) : IShellScope
        {
            private int _disposed;

            public IShell Shell => owner;
            public IServiceProvider ServiceProvider => inner.ServiceProvider;

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                try { await inner.DisposeAsync(); }
                finally { Interlocked.Decrement(ref owner._activeScopes); }
            }
        }
    }
}
