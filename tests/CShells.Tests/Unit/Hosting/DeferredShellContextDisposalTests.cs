using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;
using CShells.Tests.Integration.ShellHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Unit.Hosting;

/// <summary>
/// Tests for deferred shell context disposal:
/// when a shell is reloaded, its old <see cref="IServiceProvider"/> must not be disposed
/// while in-flight request scopes are still active (tracked via <see cref="IShellHost.AcquireContextScope"/>).
/// </summary>
public class DeferredShellContextDisposalTests
{
    // -------------------------------------------------------------------------
    // Immediate disposal (no active scopes)
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "ReloadAll disposes old context immediately when no scope handle is active")]
    public async Task ReloadAll_WithNoActiveScope_DisposesOldContextImmediately()
    {
        var shellId = new ShellId("Shell");
        var runtime = CreateRuntime(shellId, ["Core"]);
        await runtime.Manager.InitializeRuntimeAsync();

        var oldProvider = runtime.Host.GetShell(shellId).ServiceProvider;

        await runtime.Manager.ReloadAllShellsAsync();

        Assert.True(IsDisposed(oldProvider),
            "Old provider should be disposed immediately when no scope handle is active.");
    }

    // -------------------------------------------------------------------------
    // Deferred disposal (active scope present)
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "ReloadAll defers old context disposal while a scope handle is active")]
    public async Task ReloadAll_WithActiveScopeHandle_DefersOldContextDisposal()
    {
        var shellId = new ShellId("Shell");
        var runtime = CreateRuntime(shellId, ["Core"]);
        await runtime.Manager.InitializeRuntimeAsync();

        var oldContext = runtime.Host.GetShell(shellId);

        // Simulate an in-flight request that holds a scope on the old context.
        var scopeHandle = runtime.Host.AcquireContextScope(oldContext);

        await runtime.Manager.ReloadAllShellsAsync();

        // Old provider must NOT be disposed while the scope is still active.
        Assert.False(IsDisposed(oldContext.ServiceProvider),
            "Old provider must not be disposed while an active scope handle exists.");

        // Releasing the last scope should trigger the deferred disposal.
        await scopeHandle.DisposeAsync();

        Assert.True(IsDisposed(oldContext.ServiceProvider),
            "Old provider should be disposed after the last scope handle is released.");
    }

    [Fact(DisplayName = "ReloadAll defers disposal until all scope handles are released")]
    public async Task ReloadAll_WithTwoScopeHandles_DisposesAfterBothRelease()
    {
        var shellId = new ShellId("Shell");
        var runtime = CreateRuntime(shellId, ["Core"]);
        await runtime.Manager.InitializeRuntimeAsync();

        var oldContext = runtime.Host.GetShell(shellId);

        var handle1 = runtime.Host.AcquireContextScope(oldContext);
        var handle2 = runtime.Host.AcquireContextScope(oldContext);

        await runtime.Manager.ReloadAllShellsAsync();

        Assert.False(IsDisposed(oldContext.ServiceProvider));

        // First handle releases — second still active, must not dispose yet.
        await handle1.DisposeAsync();
        Assert.False(IsDisposed(oldContext.ServiceProvider),
            "Old provider must not be disposed while a second scope handle is still active.");

        // Second handle releases — disposal should fire.
        await handle2.DisposeAsync();
        Assert.True(IsDisposed(oldContext.ServiceProvider));
    }

    // -------------------------------------------------------------------------
    // Double-dispose guard on ShellContextScopeHandle
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "Double-disposing a scope handle does not corrupt the active-scope counter")]
    public async Task DoubleDisposeScopeHandle_DoesNotCorruptActiveScopeCounter()
    {
        var shellId = new ShellId("Shell");
        var runtime = CreateRuntime(shellId, ["Core"]);
        await runtime.Manager.InitializeRuntimeAsync();

        var oldContext = runtime.Host.GetShell(shellId);
        var handle = runtime.Host.AcquireContextScope(oldContext);

        await runtime.Manager.ReloadAllShellsAsync();

        // Dispose twice — the guard in ShellContextScopeHandle must prevent double-decrement.
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        // Counter must be exactly 0, not -1.
        Assert.Equal(0, oldContext.ActiveScopes);

        // Provider must have been disposed exactly once (not throw on the second DisposeAsync call).
        Assert.True(IsDisposed(oldContext.ServiceProvider));
    }

    // -------------------------------------------------------------------------
    // RemoveShell also defers disposal
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "RemoveShell defers old context disposal while a scope handle is active")]
    public async Task RemoveShell_WithActiveScopeHandle_DefersOldContextDisposal()
    {
        var shellId = new ShellId("Shell");
        var runtime = CreateRuntime(shellId, ["Core"]);
        await runtime.Manager.InitializeRuntimeAsync();

        var oldContext = runtime.Host.GetShell(shellId);
        var scopeHandle = runtime.Host.AcquireContextScope(oldContext);

        await runtime.Manager.RemoveShellAsync(shellId);

        Assert.False(IsDisposed(oldContext.ServiceProvider),
            "Old provider must not be disposed while an active scope handle exists.");

        await scopeHandle.DisposeAsync();

        Assert.True(IsDisposed(oldContext.ServiceProvider));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> if the provider has been disposed.
    /// A disposed <see cref="ServiceProvider"/> throws <see cref="ObjectDisposedException"/>
    /// on any <c>GetService</c> call.
    /// </summary>
    private static bool IsDisposed(IServiceProvider provider)
    {
        try
        {
            provider.GetService(typeof(IDisposable));
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static TestRuntime CreateRuntime(ShellId shellId, IEnumerable<string> features)
    {
        var initialSettings = new ShellSettings(shellId, features.ToArray());
        var provider = new MutableInMemoryShellSettingsProvider([initialSettings]);
        var cache = new ShellSettingsCache();
        cache.Load([initialSettings]);

        var (services, rootProvider) = TestFixtures.CreateRootServices();
        var accessor = TestFixtures.CreateRootServicesAccessor(services);
        var featureFactory = new DefaultShellFeatureFactory(rootProvider);
        var exclusionRegistry = new ShellServiceExclusionRegistry([]);
        var stateStore = new ShellRuntimeStateStore();
        var runtimeCatalog = new RuntimeFeatureCatalog(
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(TestFixtures).Assembly]),
            NullLogger<RuntimeFeatureCatalog>.Instance);

        var host = new DefaultShellHost(
            cache,
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(TestFixtures).Assembly]),
            rootProvider,
            accessor,
            featureFactory,
            exclusionRegistry,
            seedDesiredStateFromCache: true,
            runtimeFeatureCatalog: runtimeCatalog,
            runtimeStateStore: stateStore,
            notificationPublisher: new SilentNotificationPublisher(),
            logger: NullLogger<DefaultShellHost>.Instance);

        var accessorService = new ShellRuntimeStateAccessor(stateStore);
        var manager = new DefaultShellManager(
            host, cache, provider, stateStore, runtimeCatalog, accessorService,
            new SilentNotificationPublisher(), NullLogger<DefaultShellManager>.Instance);

        return new(host, manager);
    }

    private sealed record TestRuntime(DefaultShellHost Host, DefaultShellManager Manager);

    private sealed class SilentNotificationPublisher : INotificationPublisher
    {
        public Task PublishAsync<TNotification>(
            TNotification notification,
            INotificationStrategy? strategy = null,
            CancellationToken cancellationToken = default) where TNotification : class, INotification
            => Task.CompletedTask;
    }
}
