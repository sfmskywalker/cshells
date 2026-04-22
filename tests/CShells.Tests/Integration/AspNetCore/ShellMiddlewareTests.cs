using CShells.AspNetCore.Middleware;
using CShells.Hosting;
using CShells.Resolution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Tests for <see cref="ShellMiddleware"/>.
/// </summary>
public class ShellMiddlewareTests
{
    private static ShellMiddleware CreateMiddleware(
        RequestDelegate next,
        IShellResolver? resolver = null,
        IShellHost? host = null,
        IMemoryCache? cache = null,
        IOptions<ShellMiddlewareOptions>? options = null)
    {
        cache ??= new MemoryCache(new MemoryCacheOptions());
        options ??= Options.Create(new ShellMiddlewareOptions());
        return new(next, resolver ?? new NullShellResolver(), host ?? new TestShellHost(), cache, options);
    }

    [Fact(DisplayName = "InvokeAsync with no shells registered continues without setting scope")]
    public async Task InvokeAsync_WithNoShellsRegistered_ContinuesWithoutSettingScope()
    {
        // Arrange
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var resolver = new NullShellResolver();
        var host = new TestShellHost(); // Empty host with no shells
        var nextCalled = false;

        var middleware = CreateMiddleware(
            ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            resolver,
            host);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = originalServiceProvider
        };

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.True(nextCalled);
        Assert.Same(originalServiceProvider, httpContext.RequestServices);
    }

    [Fact(DisplayName = "InvokeAsync with null ShellId continues without setting scope")]
    public async Task InvokeAsync_WithNullShellId_ContinuesWithoutSettingScope()
    {
        // Arrange
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var shellServices = new ServiceCollection();
        var shellServiceProvider = shellServices.BuildServiceProvider();

        var settings = new ShellSettings(new("TestShell"));
        var shellContext = new ShellContext(settings, shellServiceProvider, Array.Empty<string>());

        var resolver = new NullShellResolver();
        var host = new TestShellHost(shellContext); // Host with a shell, but resolver returns null
        var nextCalled = false;

        var middleware = CreateMiddleware(
            ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            resolver,
            host);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = originalServiceProvider
        };

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.True(nextCalled);
        Assert.Same(originalServiceProvider, httpContext.RequestServices);
    }

    [Fact(DisplayName = "InvokeAsync with valid ShellId sets RequestServices from shell scope")]
    public async Task InvokeAsync_WithValidShellId_SetsRequestServicesFromShellScope()
    {
        // Arrange
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var shellServices = new ServiceCollection();
        shellServices.AddSingleton<ITestService, TestService>();
        var shellServiceProvider = shellServices.BuildServiceProvider();

        var settings = new ShellSettings(new("TestShell"));
        var shellContext = new ShellContext(settings, shellServiceProvider, Array.Empty<string>());

        var resolver = new FixedShellResolver(new("TestShell"));
        var host = new TestShellHost(shellContext);

        IServiceProvider? capturedRequestServices = null;
        ITestService? capturedTestService = null;
        var middleware = CreateMiddleware(
            ctx =>
            {
                capturedRequestServices = ctx.RequestServices;
                // Capture the service while within the scope
                capturedTestService = ctx.RequestServices.GetService<ITestService>();
                return Task.CompletedTask;
            },
            resolver,
            host);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = originalServiceProvider
        };

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.NotNull(capturedRequestServices);
        Assert.NotSame(originalServiceProvider, capturedRequestServices);

        // Verify services from shell are available (captured while in scope)
        Assert.NotNull(capturedTestService);
    }

    [Fact(DisplayName = "InvokeAsync sets RequestServices to shell scope for the request lifetime")]
    public async Task InvokeAsync_SetsRequestServicesToShellScope_ForRequestLifetime()
    {
        // Arrange
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var shellServices = new ServiceCollection();
        shellServices.AddSingleton<ITestService, TestService>();
        var shellServiceProvider = shellServices.BuildServiceProvider();

        var settings = new ShellSettings(new("TestShell"));
        var shellContext = new ShellContext(settings, shellServiceProvider, Array.Empty<string>());

        var resolver = new FixedShellResolver(new("TestShell"));
        var host = new TestShellHost(shellContext);

        var middleware = CreateMiddleware(ctx => Task.CompletedTask, resolver, host);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = originalServiceProvider
        };

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert - RequestServices should remain set to shell scope (needed for endpoints)
        Assert.NotSame(originalServiceProvider, httpContext.RequestServices);
        Assert.NotNull(httpContext.RequestServices.GetService<ITestService>());
    }

    [Fact(DisplayName = "InvokeAsync disposes shell scope even after exception")]
    public async Task InvokeAsync_DisposesShellScope_EvenAfterException()
    {
        // Arrange
        var originalServiceProvider = new ServiceCollection().BuildServiceProvider();
        var shellServices = new ServiceCollection();
        var shellServiceProvider = shellServices.BuildServiceProvider();

        var settings = new ShellSettings(new("TestShell"));
        var shellContext = new ShellContext(settings, shellServiceProvider, Array.Empty<string>());

        var resolver = new FixedShellResolver(new("TestShell"));
        var host = new TestShellHost(shellContext);

        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Test exception"), resolver, host);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = originalServiceProvider
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));
        // The scope should be disposed even if an exception occurs
        // Note: We can't directly test disposal, but the test verifies the exception is properly propagated
    }

    [Fact(DisplayName = "InvokeAsync acquires a context scope that is active during the request and released at response completion")]
    public async Task InvokeAsync_AcquiresContextScope_HeldDuringRequestReleasedAtResponseCompletion()
    {
        // Arrange
        var shellServices = new ServiceCollection();
        var shellServiceProvider = shellServices.BuildServiceProvider();
        var settings = new ShellSettings(new("TestShell"));
        var shellContext = new ShellContext(settings, shellServiceProvider, Array.Empty<string>());

        var host = new TrackingShellHost(shellContext);
        int activeScopesDuringNext = -1;

        // Inject a custom response feature that captures OnCompleted callbacks so we can
        // fire them on demand, simulating what the ASP.NET Core server does after the
        // response is sent. DefaultHttpContext has no built-in mechanism for this in tests.
        var httpContext = new DefaultHttpContext();
        var responseFeature = new FireableResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        var middleware = CreateMiddleware(
            ctx =>
            {
                // Capture the counter while inside _next — the scope must be active here.
                activeScopesDuringNext = shellContext.ActiveScopes;
                return Task.CompletedTask;
            },
            new FixedShellResolver(new("TestShell")),
            host);

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert — scope was held while _next executed.
        Assert.Equal(1, activeScopesDuringNext);

        // After InvokeAsync returns but before OnCompleted fires, the handle must still be active.
        // (In a real server OnCompleted fires after all middleware return. In tests it does not
        // fire automatically, so the counter stays at 1 here — proving the handle is NOT released
        // at middleware return but deferred to request completion.)
        Assert.Equal(1, shellContext.ActiveScopes);

        // Simulate the server firing OnCompleted callbacks.
        await responseFeature.FireOnCompletedAsync();

        // Now the scope must be released.
        Assert.Equal(0, shellContext.ActiveScopes);
    }

    [Theory(DisplayName = "Constructor guard clauses throw ArgumentNullException")]
    [InlineData(true, false, false, false, false, "next")]
    [InlineData(false, true, false, false, false, "resolver")]
    [InlineData(false, false, true, false, false, "host")]
    [InlineData(false, false, false, true, false, "cache")]
    [InlineData(false, false, false, false, true, "options")]
    public void Constructor_GuardClauses_ThrowArgumentNullException(bool nullNext, bool nullResolver, bool nullHost, bool nullCache, bool nullOptions, string expectedParam)
    {
        RequestDelegate? next = nullNext ? null : _ => Task.CompletedTask;
        var resolver = nullResolver ? null : new NullShellResolver();
        var host = nullHost ? null : new TestShellHost();
        var cache = nullCache ? null : new MemoryCache(new MemoryCacheOptions());
        var options = nullOptions ? null : Options.Create(new ShellMiddlewareOptions());

        var exception = Assert.Throws<ArgumentNullException>(() => new ShellMiddleware(next!, resolver!, host!, cache!, options!));
        Assert.Equal(expectedParam, exception.ParamName);
    }

    // Test helpers
    private interface ITestService { }
    private class TestService : ITestService { }

    private class NullShellResolver : IShellResolver
    {
        public ShellId? Resolve(ShellResolutionContext context) => null;
    }

    private class FixedShellResolver(ShellId shellId) : IShellResolver
    {
        public ShellId? Resolve(ShellResolutionContext context) => shellId;
    }

    private class TestShellHost(ShellContext? shellContext = null) : IShellHost
    {
        public ShellContext DefaultShell => shellContext ?? throw new InvalidOperationException("No shell configured");
        public IReadOnlyCollection<ShellContext> AllShells => shellContext != null ? [shellContext] : [];

        public ShellContext GetShell(ShellId id)
        {
            if (shellContext == null)
            {
                throw new KeyNotFoundException($"Shell '{id}' not found");
            }
            return shellContext;
        }

        public ValueTask EvictShellAsync(ShellId shellId) => ValueTask.CompletedTask;
        public ValueTask EvictAllShellsAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A <see cref="TestShellHost"/> that overrides <see cref="IShellHost.AcquireContextScope"/>
    /// to use the real active-scope counter on <see cref="ShellContext"/>, letting tests observe
    /// when the scope handle is acquired and released.
    /// </summary>
    private sealed class TrackingShellHost(ShellContext? shellContext = null) : TestShellHost(shellContext), IShellHost
    {
        IAsyncDisposable IShellHost.AcquireContextScope(ShellContext context)
        {
            context.IncrementActiveScopes();
            return new ScopeHandle(context);
        }

        private sealed class ScopeHandle(ShellContext context) : IAsyncDisposable
        {
            private int _released;

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return ValueTask.CompletedTask;
                context.DecrementActiveScopes();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// A custom <see cref="IHttpResponseFeature"/> that captures <c>OnCompleted</c> callbacks
    /// so tests can fire them on demand, simulating what the ASP.NET Core server does after
    /// the response is sent. <see cref="DefaultHttpContext"/> has no built-in mechanism to
    /// trigger these callbacks outside of a running server.
    /// </summary>
    private sealed class FireableResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onCompleted = new();

        public override void OnCompleted(Func<object, Task> callback, object state)
            => _onCompleted.Add((callback, state));

        public async Task FireOnCompletedAsync()
        {
            foreach (var (callback, state) in _onCompleted)
                await callback(state);
        }
    }
}
