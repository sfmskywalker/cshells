using CShells.Configuration;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;
using CShells.Tests.Integration.ShellHost;
using CShells.Tests.TestHelpers;

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
        var context1 = host.GetShell(tenant1);
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

    [Fact(DisplayName = "After invalidation of removed shell, GetShell throws KeyNotFoundException")]
    public async Task AfterInvalidation_RemovedShell_ThrowsKeyNotFound()
    {
        // Arrange
        var shellId = new ShellId("ToRemove");
        var settings = new ShellSettings(shellId);
        var cache = fixture.CreateCache([settings]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);

        // Build the shell
        _ = host.GetShell(shellId);

        // Remove from cache and invalidate context
        cache.Load([]); // empty cache
        await host.EvictShellAsync(shellId);

        // Act & Assert - shell should no longer be accessible
        Assert.Throws<KeyNotFoundException>(() => host.GetShell(shellId));
    }

    #endregion

    #region US3 - Notification Integration

    [Fact(DisplayName = "ReloadShellAsync through manager emits ShellReloading and ShellReloaded with real host")]
    public async Task ReloadShellAsync_ThroughManager_EmitsNotificationsWithRealHost()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var settings = new ShellSettings(shellId, ["Weather"]);

        var cache = fixture.CreateCache([settings]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);
        var provider = new InMemoryShellSettingsProvider([settings]);
        var notifications = new RecordingNotificationPublisher();
        var manager = new DefaultShellManager(host, cache, provider, notifications);

        // Build the shell initially
        _ = host.GetShell(shellId);

        // Act
        await manager.ReloadShellAsync(shellId);

        // Assert - reload notifications
        var reloading = notifications.Notifications.OfType<ShellReloading>().ToList();
        var reloaded = notifications.Notifications.OfType<ShellReloaded>().ToList();
        Assert.Single(reloading);
        Assert.Single(reloaded);
        Assert.Equal(shellId, reloading[0].ShellId);
        Assert.Equal(shellId, reloaded[0].ShellId);

        // Assert - lifecycle notifications
        Assert.Single(notifications.Notifications.OfType<ShellDeactivating>());
        Assert.Single(notifications.Notifications.OfType<ShellActivated>());
    }

    [Fact(DisplayName = "ReloadAllShellsAsync through manager emits aggregate notifications and rebuilds shells")]
    public async Task ReloadAllShellsAsync_ThroughManager_EmitsAggregateNotificationsAndRebuilds()
    {
        // Arrange
        var tenant1 = new ShellId("Tenant1");
        var tenant2 = new ShellId("Tenant2");
        var settings1 = new ShellSettings(tenant1, ["Weather"]);
        var settings2 = new ShellSettings(tenant2);

        var cache = fixture.CreateCache([settings1, settings2]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);
        var provider = new InMemoryShellSettingsProvider([settings1, settings2]);
        var notifications = new RecordingNotificationPublisher();
        var manager = new DefaultShellManager(host, cache, provider, notifications);

        // Build shells initially
        var ctx1Before = host.GetShell(tenant1);
        var ctx2Before = host.GetShell(tenant2);

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert - aggregate notifications
        Assert.Contains(notifications.Notifications, n => n is ShellReloading r && r.ShellId is null);
        Assert.Contains(notifications.Notifications, n => n is ShellReloaded r && r.ShellId is null);

        // Assert - lifecycle notifications for both shells
        var deactivating = notifications.Notifications.OfType<ShellDeactivating>().ToList();
        var activated = notifications.Notifications.OfType<ShellActivated>().ToList();
        Assert.Equal(2, deactivating.Count);
        Assert.Equal(2, activated.Count);

        // Assert - shells are rebuilt
        var ctx1After = host.GetShell(tenant1);
        var ctx2After = host.GetShell(tenant2);
        Assert.NotSame(ctx1Before, ctx1After);
        Assert.NotSame(ctx2Before, ctx2After);
    }

    [Fact(DisplayName = "Lifecycle notifications remain in expected sequence around reload notifications")]
    public async Task ReloadShellAsync_LifecycleNotifications_PreservedSequence()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var settings = new ShellSettings(shellId, ["Weather"]);

        var cache = fixture.CreateCache([settings]);
        var host = fixture.CreateHost(cache, typeof(TestFixtures).Assembly);
        var provider = new InMemoryShellSettingsProvider([settings]);
        var notifications = new RecordingNotificationPublisher();
        var manager = new DefaultShellManager(host, cache, provider, notifications);

        // Build shell initially
        _ = host.GetShell(shellId);

        // Act
        await manager.ReloadShellAsync(shellId);

        // Assert - full sequence: ShellReloading → ShellDeactivating → ShellActivated → ShellReloaded
        var allNotifications = notifications.Notifications.ToList();
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

        public Task PublishAsync<TNotification>(
            TNotification notification,
            INotificationStrategy? strategy = null,
            CancellationToken cancellationToken = default) where TNotification : class, INotification
        {
            _notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
