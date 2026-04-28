using CShells.AspNetCore.Middleware;
using CShells.AspNetCore.Routing;
using CShells.Lifecycle;
using CShells.Resolution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Tests for <see cref="ShellMiddleware"/> — the middleware that resolves a shell per request
/// and sets <see cref="HttpContext.RequestServices"/> to a scope from that shell's provider.
/// The scope is released via <see cref="HttpResponse.OnCompleted"/> so upstream middleware can
/// still read RequestServices during post-_next processing.
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

        var (httpContext, responseFeature) = CreateHttpContextWithFireableResponse(originalServiceProvider);

        await middleware.InvokeAsync(httpContext);

        Assert.NotNull(capturedRequestServices);
        Assert.NotSame(originalServiceProvider, capturedRequestServices);
        Assert.NotNull(capturedService);

        // Fire OnCompleted so the scope releases, then verify the counter dropped to zero.
        await responseFeature.FireOnCompletedAsync();
        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Fact(DisplayName = "Scope is held during _next and released via Response.OnCompleted, not at InvokeAsync return")]
    public async Task Scope_HeldDuringRequest_ReleasedAtResponseCompletion()
    {
        var shell = FakeShell.WithServices(_ => { }, name: "TestShell");
        var registry = new FakeRegistry(shell);

        int activeScopesDuringNext = -1;
        var middleware = CreateMiddleware(
            _ => { activeScopesDuringNext = shell.ActiveScopeCount; return Task.CompletedTask; },
            resolver: new FixedShellResolver("TestShell"),
            registry: registry);

        var (httpContext, responseFeature) = CreateHttpContextWithFireableResponse();

        await middleware.InvokeAsync(httpContext);

        // Scope was active during _next.
        Assert.Equal(1, activeScopesDuringNext);

        // Scope is STILL held after InvokeAsync returns — deferred to OnCompleted so upstream
        // middleware can read RequestServices during its post-_next work.
        Assert.Equal(1, shell.ActiveScopeCount);

        // Simulate the server firing OnCompleted callbacks after the response is written.
        await responseFeature.FireOnCompletedAsync();

        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Fact(DisplayName = "Downstream exception propagates; scope is released when OnCompleted fires on error paths")]
    public async Task DownstreamException_Propagates()
    {
        var shell = FakeShell.WithServices(_ => { }, name: "TestShell");
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("boom"),
            resolver: new FixedShellResolver("TestShell"),
            registry: new FakeRegistry(shell));

        var (httpContext, responseFeature) = CreateHttpContextWithFireableResponse();

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));

        // In a real server, OnCompleted fires even on error paths once the response is finalized.
        // Simulate that here; the scope should release cleanly.
        await responseFeature.FireOnCompletedAsync();
        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Theory(DisplayName = "Constructor guard clauses throw ArgumentNullException")]
    [InlineData(true, false, false, false, false, false, "next")]
    [InlineData(false, true, false, false, false, false, "resolver")]
    [InlineData(false, false, true, false, false, false, "registry")]
    [InlineData(false, false, false, true, false, false, "endpointDataSource")]
    [InlineData(false, false, false, false, true, false, "cache")]
    [InlineData(false, false, false, false, false, true, "options")]
    public void Constructor_GuardClauses_ThrowArgumentNullException(
        bool nullNext, bool nullResolver, bool nullRegistry, bool nullDataSource, bool nullCache, bool nullOptions, string expectedParam)
    {
        RequestDelegate? next = nullNext ? null : _ => Task.CompletedTask;
        IShellResolver? resolver = nullResolver ? null : new NullShellResolver();
        IShellRegistry? registry = nullRegistry ? null : new FakeRegistry();
        DynamicShellEndpointDataSource? dataSource = nullDataSource ? null : new DynamicShellEndpointDataSource();
        IMemoryCache? cache = nullCache ? null : new MemoryCache(new MemoryCacheOptions());
        IOptions<ShellMiddlewareOptions>? options = nullOptions ? null : Options.Create(new ShellMiddlewareOptions());

        var ex = Assert.Throws<ArgumentNullException>(() => new ShellMiddleware(next!, resolver!, registry!, dataSource!, cache!, options!));
        Assert.Equal(expectedParam, ex.ParamName);
    }

    // =================================================================
    // Test doubles
    // =================================================================

    private static ShellMiddleware CreateMiddleware(
        RequestDelegate next,
        IShellResolver? resolver = null,
        IShellRegistry? registry = null,
        DynamicShellEndpointDataSource? endpointDataSource = null,
        IMemoryCache? cache = null,
        IOptions<ShellMiddlewareOptions>? options = null) =>
        new(
            next,
            resolver ?? new NullShellResolver(),
            registry ?? new FakeRegistry(),
            endpointDataSource ?? new DynamicShellEndpointDataSource(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            options ?? Options.Create(new ShellMiddlewareOptions()));

    private static (DefaultHttpContext Context, FireableResponseFeature Response) CreateHttpContextWithFireableResponse(
        IServiceProvider? requestServices = null)
    {
        var ctx = new DefaultHttpContext();
        if (requestServices is not null)
            ctx.RequestServices = requestServices;
        var response = new FireableResponseFeature();
        ctx.Features.Set<IHttpResponseFeature>(response);
        return (ctx, response);
    }

    private interface ITestService;

    private sealed class TestService : ITestService;

    private sealed class NullShellResolver : IShellResolver
    {
        public Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult<ShellId?>(null);
    }

    private sealed class FixedShellResolver(ShellId shellId) : IShellResolver
    {
        public Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult<ShellId?>(shellId);
    }

    internal sealed class FakeRegistry(FakeShell? shell = null) : IShellRegistry
    {
        private readonly FakeShell? _shell = shell;

        public Task<IShell> GetOrActivateAsync(string name, CancellationToken ct = default)
            => GetActive(name) is { } active
                ? Task.FromResult(active)
                : Task.FromException<IShell>(new ShellBlueprintNotFoundException(name));
        public Task<IShell> ActivateAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken ct = default) => Task.FromResult<ProvidedBlueprint?>(null);
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken ct = default) => Task.FromResult<IShellBlueprintManager?>(null);
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken ct = default) => Task.FromResult(new ShellPage([], null));
        public IShell? GetActive(string name)
            => _shell is not null && string.Equals(_shell.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase) ? _shell : null;
        public IReadOnlyCollection<IShell> GetAll(string name) => _shell is null ? [] : [_shell];
        public IReadOnlyCollection<IShell> GetActiveShells() => _shell is null ? [] : [_shell];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }

    internal sealed class FakeShell(ShellDescriptor descriptor, IServiceProvider provider) : IShell
    {
        private int _activeScopes;

        public ShellDescriptor Descriptor { get; } = descriptor;
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider { get; } = provider;
        public IDrainOperation? Drain => null;
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

    /// <summary>
    /// A custom <see cref="HttpResponseFeature"/> that captures <c>OnCompleted</c> callbacks so
    /// tests can fire them on demand, simulating what the ASP.NET Core server does after the
    /// response is sent. <see cref="DefaultHttpContext"/> has no built-in mechanism to trigger
    /// these callbacks outside of a running server.
    /// </summary>
    internal sealed class FireableResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onCompleted = [];

        public override void OnCompleted(Func<object, Task> callback, object state)
            => _onCompleted.Add((callback, state));

        public async Task FireOnCompletedAsync()
        {
            foreach (var (callback, state) in _onCompleted)
                await callback(state);
        }
    }
}
