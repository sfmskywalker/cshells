using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;
using CShells.Tests.Integration.ShellHost;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Integration.DefaultShellHost;

/// <summary>
/// Integration tests for shell reload behavior at the host level.
/// Tests verify that cache invalidation + settings update results in rebuilt shell contexts.
/// </summary>
[Collection(nameof(DefaultShellHostCollection))]
public class ReloadBehaviorTests(DefaultShellHostFixture fixture)
{
    #region US1 - Targeted Reload Rebuild Behavior

    [Fact(DisplayName = "After invalidation and cache update, GetShell returns rebuilt context")]
    public async Task AfterInvalidationAndCacheUpdate_GetShell_ReturnsRebuiltContext()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var originalSettings = new ShellSettings(shellId, ["Weather"]);
        var cache = fixture.CreateCache([originalSettings]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);

        // Build the shell initially
        var originalContext = host.GetShell(shellId);
        Assert.NotNull(originalContext);

        // Update the cache with new settings (simulating what DefaultShellManager does)
        var updatedSettings = new ShellSettings(shellId, ["Weather"]);
        cache.Load(cache.GetAll().Where(s => !s.Id.Equals(shellId)).Append(updatedSettings).ToList());

        // Invalidate the cached context
        await host.EvictShellAsync(shellId);

        // Act - next access should rebuild
        var rebuiltContext = host.GetShell(shellId);

        // Assert - should be a different instance (rebuilt)
        Assert.NotSame(originalContext, rebuiltContext);
        Assert.NotNull(rebuiltContext.ServiceProvider);
    }

    [Fact(DisplayName = "After invalidation of one shell, unrelated shells retain their context")]
    public async Task AfterInvalidation_UnrelatedShells_RetainContext()
    {
        // Arrange
        var tenant1 = new ShellId("Tenant1");
        var tenant2 = new ShellId("Tenant2");
        var settings = new[]
        {
            new ShellSettings(tenant1),
            new ShellSettings(tenant2)
        };
        var cache = fixture.CreateCache(settings);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);

        // Build both shells
        _ = host.GetShell(tenant1);
        var context2Before = host.GetShell(tenant2);

        // Act - invalidate only Tenant1
        await host.EvictShellAsync(tenant1);

        // Assert - Tenant2 should still be the same instance
        var context2After = host.GetShell(tenant2);
        Assert.Same(context2Before, context2After);
    }

    [Fact(DisplayName = "Invalidating a shell that was never built is a no-op")]
    public async Task Invalidation_UnbuiltShell_IsNoOp()
    {
        // Arrange
        var shellId = new ShellId("NeverBuilt");
        var settings = new ShellSettings(shellId);
        var cache = fixture.CreateCache([settings]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);

        // Act - invalidate a shell that was never built (should not throw)
        await host.EvictShellAsync(shellId);

        // Assert - shell should still be buildable
        var context = host.GetShell(shellId);
        Assert.NotNull(context);
    }

    [Fact(DisplayName = "After invalidation without cache update, GetShell rebuilds from existing cache")]
    public async Task AfterInvalidation_WithoutCacheUpdate_RebuildsFromExistingCache()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var originalSettings = new ShellSettings(shellId, ["Weather"]);
        var cache = fixture.CreateCache([originalSettings]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);

        var originalContext = host.GetShell(shellId);

        // Act - invalidate without updating cache
        await host.EvictShellAsync(shellId);
        var rebuiltContext = host.GetShell(shellId);

        // Assert - should be a different instance but with same settings
        Assert.NotSame(originalContext, rebuiltContext);
        Assert.Equal(originalContext.Settings.Id, rebuiltContext.Settings.Id);
    }

    #endregion

    #region US2 - Full Reload Stale Context Invalidation

    [Fact(DisplayName = "InvalidateAllShellContextsAsync rebuilds all shells on next access")]
    public async Task InvalidateAll_RebuildsAllShells_OnNextAccess()
    {
        // Arrange
        var tenant1 = new ShellId("Tenant1");
        var tenant2 = new ShellId("Tenant2");
        var settings = new[]
        {
            new ShellSettings(tenant1),
            new ShellSettings(tenant2)
        };
        var cache = fixture.CreateCache(settings);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);

        // Build both shells
        var context1Before = host.GetShell(tenant1);
        var context2Before = host.GetShell(tenant2);

        // Act - invalidate all
        await host.EvictAllShellsAsync();

        // Assert - next access returns fresh instances
        var context1After = host.GetShell(tenant1);
        var context2After = host.GetShell(tenant2);
        Assert.NotSame(context1Before, context1After);
        Assert.NotSame(context2Before, context2After);
    }

    [Fact(DisplayName = "After invalidation, an applied shell remains rebuildable even if it was removed from desired cache")]
    public async Task AfterInvalidation_RemovedFromDesiredCache_AppliedRuntimeRemainsBuildable()
    {
        // Arrange
        var shellId = new ShellId("ToRemove");
        var settings = new ShellSettings(shellId, ["Core"]);
        var cache = fixture.CreateCache([settings]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);

        // Build the shell
        var originalContext = host.GetShell(shellId);

        // Remove from desired cache only and invalidate the materialized context.
        // The applied runtime record should remain authoritative until explicit runtime removal.
        cache.Load([]); // empty cache
        await host.EvictShellAsync(shellId);

        // Act
        var rebuiltContext = host.GetShell(shellId);

        // Assert
        Assert.NotSame(originalContext, rebuiltContext);
        Assert.Equal(shellId, rebuiltContext.Id);
        Assert.Equal(["Core"], rebuiltContext.EnabledFeatures);
    }

    #endregion

    #region US3 - Notification Integration

    [Fact(DisplayName = "ReloadShellAsync through manager emits reload plus applied-runtime lifecycle notifications when a successor commits")]
    public async Task ReloadShellAsync_ThroughManager_EmitsNotificationsWithSharedHost()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var settings = new ShellSettings(shellId, ["Weather"]);

        var runtime = CreateManagedRuntime([settings], new InMemoryShellSettingsProvider([settings]));

        // Build the shell initially
        await runtime.Manager.InitializeRuntimeAsync();
        _ = runtime.Host.GetShell(shellId);
        runtime.Notifications.Clear();

        // Act
        await runtime.Manager.ReloadShellAsync(shellId);

        // Assert - reload notifications
        var reloading = runtime.Notifications.Notifications.OfType<ShellReloading>().ToList();
        var reloaded = runtime.Notifications.Notifications.OfType<ShellReloaded>().ToList();
        Assert.Single(reloading);
        Assert.Single(reloaded);
        Assert.Equal(shellId, reloading[0].ShellId);
        Assert.Equal(shellId, reloaded[0].ShellId);

        // Assert - lifecycle notifications
        Assert.Single(runtime.Notifications.Notifications.OfType<ShellDeactivating>());
        Assert.Single(runtime.Notifications.Notifications.OfType<ShellActivated>());
    }

    [Fact(DisplayName = "ReloadAllShellsAsync through manager rebuilds all shells including those with missing features")]
    public async Task ReloadAllShellsAsync_ThroughManager_ReconcilesMixedShellsAtomically()
    {
        // Arrange
        var tenant1 = new ShellId("Tenant1");
        var tenant2 = new ShellId("Tenant2");
        var initialSettings1 = new ShellSettings(tenant1, ["Core"]);
        var initialSettings2 = new ShellSettings(tenant2, ["Core"]);
        var updatedSettings1 = new ShellSettings(tenant1, ["Weather"]);
        var partialSettings2 = new ShellSettings(tenant2, ["Core", "MissingFeature"]);

        var runtime = CreateManagedRuntime(
            [initialSettings1, initialSettings2],
            new InMemoryShellSettingsProvider([updatedSettings1, partialSettings2]));

        // Build shells initially
        await runtime.Manager.InitializeRuntimeAsync();
        var ctx1Before = runtime.Host.GetShell(tenant1);
        var ctx2Before = runtime.Host.GetShell(tenant2);
        runtime.Notifications.Clear();

        // Act
        await runtime.Manager.ReloadAllShellsAsync();

        // Assert - aggregate notifications
        Assert.Contains(runtime.Notifications.Notifications, n => n is ShellReloading r && r.ShellId is null);
        Assert.Contains(runtime.Notifications.Notifications, n => n is ShellReloaded r && r.ShellId is null);

        // Assert - lifecycle notifications for BOTH shells (both rebuild; Tenant2 rebuilds with available features)
        var deactivating = runtime.Notifications.Notifications.OfType<ShellDeactivating>().ToList();
        var activated = runtime.Notifications.Notifications.OfType<ShellActivated>().ToList();
        Assert.Equal(2, deactivating.Count);
        Assert.Equal(2, activated.Count);

        // Assert - both shells are rebuilt
        var ctx1After = runtime.Host.GetShell(tenant1);
        var ctx2After = runtime.Host.GetShell(tenant2);
        Assert.NotSame(ctx1Before, ctx1After);
        Assert.NotSame(ctx2Before, ctx2After);

        var tenant2Status = runtime.Accessor.GetShell(tenant2);
        Assert.NotNull(tenant2Status);
        Assert.True(tenant2Status.IsRoutable);
        Assert.True(tenant2Status.IsInSync);
        Assert.Equal(2, tenant2Status.AppliedGeneration);
        Assert.Equal(ShellReconciliationOutcome.ActiveWithMissingFeatures, tenant2Status.Outcome);
        Assert.Equal(["MissingFeature"], tenant2Status.MissingFeatures);
    }

    [Fact(DisplayName = "Lifecycle notifications remain in expected sequence around reload notifications")]
    public async Task ReloadShellAsync_LifecycleNotifications_PreservedSequence()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var settings = new ShellSettings(shellId, ["Weather"]);

        var runtime = CreateManagedRuntime([settings], new InMemoryShellSettingsProvider([settings]));

        // Build shell initially
        await runtime.Manager.InitializeRuntimeAsync();
        _ = runtime.Host.GetShell(shellId);
        runtime.Notifications.Clear();

        // Act
        await runtime.Manager.ReloadShellAsync(shellId);

        // Assert - full sequence: ShellReloading → ShellDeactivating → ShellActivated → ShellReloaded
        var allNotifications = runtime.Notifications.Notifications.ToList();
        var reloadingIdx = allNotifications.FindIndex(n => n is ShellReloading);
        var deactivatingIdx = allNotifications.FindIndex(n => n is ShellDeactivating);
        var activatedIdx = allNotifications.FindIndex(n => n is ShellActivated);
        var reloadedIdx = allNotifications.FindIndex(n => n is ShellReloaded);

        Assert.True(reloadingIdx >= 0, "ShellReloading should be emitted");
        Assert.True(deactivatingIdx >= 0, "ShellDeactivating should be emitted");
        Assert.True(activatedIdx >= 0, "ShellActivated should be emitted");
        Assert.True(reloadedIdx >= 0, "ShellReloaded should be emitted");

        Assert.True(reloadingIdx < deactivatingIdx, "ShellReloading must precede ShellDeactivating");
        Assert.True(deactivatingIdx < activatedIdx, "ShellDeactivating must precede ShellActivated");
        Assert.True(activatedIdx < reloadedIdx, "ShellActivated must precede ShellReloaded");
    }

    #endregion

    /// <summary>
    /// A notification publisher that records all notifications in order for assertion.
    /// </summary>
    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        private readonly List<object> _notifications = [];
        public IReadOnlyList<object> Notifications => _notifications;

        public void Clear() => _notifications.Clear();

        public Task PublishAsync<TNotification>(
            TNotification notification,
            INotificationStrategy? strategy = null,
            CancellationToken cancellationToken = default) where TNotification : class, INotification
        {
            _notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private TestRuntime CreateManagedRuntime(
        IEnumerable<ShellSettings> initialShells,
        IShellSettingsProvider provider)
    {
        var cache = fixture.CreateCache(initialShells);
        var notifications = new RecordingNotificationPublisher();
        var stateStore = new ShellRuntimeStateStore();
        var runtimeCatalog = new RuntimeFeatureCatalog(
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(TestFixtures).Assembly]),
            NullLogger<RuntimeFeatureCatalog>.Instance);
        var host = new Hosting.DefaultShellHost(
            cache,
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(TestFixtures).Assembly]),
            fixture.RootProvider,
            fixture.RootAccessor,
            fixture.FeatureFactory,
            new ShellServiceExclusionRegistry([]),
            seedDesiredStateFromCache: true,
            runtimeFeatureCatalog: runtimeCatalog,
            runtimeStateStore: stateStore,
            notificationPublisher: notifications,
            logger: NullLogger<Hosting.DefaultShellHost>.Instance);
        var accessor = new ShellRuntimeStateAccessor(stateStore);
        var manager = new DefaultShellManager(
            host,
            cache,
            provider,
            stateStore,
            runtimeCatalog,
            accessor,
            notifications,
            NullLogger<DefaultShellManager>.Instance);

        return new(host, manager, accessor, notifications);
    }

    private sealed record TestRuntime(
        Hosting.DefaultShellHost Host,
        DefaultShellManager Manager,
        IShellRuntimeStateAccessor Accessor,
        RecordingNotificationPublisher Notifications);
}
