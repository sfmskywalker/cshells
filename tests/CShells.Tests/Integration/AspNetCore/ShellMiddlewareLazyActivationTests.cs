using CShells.AspNetCore.Middleware;
using CShells.Lifecycle;
using CShells.Resolution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Feature-007 US4 tests: middleware translates registry exceptions into HTTP responses and
/// performs lazy activation on first touch.
/// </summary>
public class ShellMiddlewareLazyActivationTests
{
    [Fact(DisplayName = "Request for an unknown shell name → 404")]
    public async Task Request_UnknownShell_Returns404()
    {
        var nextCalled = false;
        var middleware = new ShellMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            new FixedShellResolver("unknown"),
            new ThrowingRegistry(new ShellBlueprintNotFoundException("unknown")),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new ShellMiddlewareOptions()));

        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IHttpResponseFeature>(new HttpResponseFeature());

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.False(nextCalled, "Next delegate should NOT be invoked on 404.");
    }

    [Fact(DisplayName = "Request when provider unavailable → 503")]
    public async Task Request_ProviderUnavailable_Returns503()
    {
        var nextCalled = false;
        var inner = new InvalidOperationException("db down");
        var middleware = new ShellMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            new FixedShellResolver("flaky"),
            new ThrowingRegistry(new ShellBlueprintUnavailableException("flaky", inner)),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new ShellMiddlewareOptions()));

        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IHttpResponseFeature>(new HttpResponseFeature());

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact(DisplayName = "Successful GetOrActivateAsync sets RequestServices and invokes next")]
    public async Task Request_ActivationSucceeds_ChainsToNext()
    {
        var nextCalled = false;
        var shell = ShellMiddlewareTests.FakeShell.WithServices(_ => { }, name: "acme");
        var registry = new ShellMiddlewareTests.FakeRegistry(shell);

        var middleware = new ShellMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            new FixedShellResolver("acme"),
            registry,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new ShellMiddlewareOptions()));

        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IHttpResponseFeature>(new HttpResponseFeature());

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
    }

    // =================================================================
    // Test doubles
    // =================================================================

    private sealed class FixedShellResolver(string name) : IShellResolver
    {
        public ShellId? Resolve(ShellResolutionContext context) => new(name);
    }

    /// <summary>Minimal registry that throws a preset exception from GetOrActivateAsync.</summary>
    private sealed class ThrowingRegistry(Exception toThrow) : IShellRegistry
    {
        public Task<IShell> GetOrActivateAsync(string name, CancellationToken ct = default) =>
            Task.FromException<IShell>(toThrow);
        public Task<IShell> ActivateAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken ct = default) => Task.FromResult<ProvidedBlueprint?>(null);
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken ct = default) => Task.FromResult<IShellBlueprintManager?>(null);
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken ct = default) => Task.FromResult(new ShellPage([], null));
        public IShell? GetActive(string name) => null;
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }
}
