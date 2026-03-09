using CShells.Configuration;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;

namespace CShells.Tests.Unit.Management;

/// <summary>
/// Unit tests for <see cref="DefaultShellManager"/> reload semantics.
/// Tests use stub collaborators to verify manager behavior in isolation.
/// </summary>
public class DefaultShellManagerReloadTests
{
    #region US1 - Strict Targeted Reload

    [Fact(DisplayName = "ReloadShellAsync refreshes active shell from provider")]
    public async Task ReloadShellAsync_ActiveShell_RefreshesFromProvider()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var originalSettings = CreateShell("Tenant1", ["FeatureA"]);
        var updatedSettings = CreateShell("Tenant1", ["FeatureA", "FeatureB"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(shellId, updatedSettings);

        var cache = new ShellSettingsCache();
        cache.Load([originalSettings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadShellAsync(shellId);

        // Assert - cache should contain the updated settings
        var cached = cache.GetById(shellId);
        Assert.NotNull(cached);
        Assert.Contains("FeatureB", cached.EnabledFeatures);
    }

    [Fact(DisplayName = "ReloadShellAsync with missing provider result throws and preserves state")]
    public async Task ReloadShellAsync_MissingFromProvider_ThrowsAndPreservesState()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var originalSettings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider(); // returns null for any lookup
        var cache = new ShellSettingsCache();
        cache.Load([originalSettings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act & Assert - should throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ReloadShellAsync(shellId));

        // State should be preserved - original shell still in cache
        var cached = cache.GetById(shellId);
        Assert.NotNull(cached);
        Assert.Equal(["FeatureA"], cached.EnabledFeatures);
    }

    [Fact(DisplayName = "ReloadShellAsync does not affect unrelated shells in cache")]
    public async Task ReloadShellAsync_DoesNotAffectUnrelatedShells()
    {
        // Arrange
        var target = new ShellId("Tenant1");
        var other = new ShellId("Tenant2");
        var tenant1Settings = CreateShell("Tenant1", ["FeatureA"]);
        var tenant2Settings = CreateShell("Tenant2", ["FeatureX"]);
        var updatedTenant1 = CreateShell("Tenant1", ["FeatureA", "FeatureB"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(target, updatedTenant1);

        var cache = new ShellSettingsCache();
        cache.Load([tenant1Settings, tenant2Settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadShellAsync(target);

        // Assert - Tenant2 should remain unchanged
        var cachedTenant2 = cache.GetById(other);
        Assert.NotNull(cachedTenant2);
        Assert.Equal(["FeatureX"], cachedTenant2.EnabledFeatures);
    }

    [Fact(DisplayName = "ReloadShellAsync with new shell not yet in cache adds it")]
    public async Task ReloadShellAsync_NewShell_AddsToCache()
    {
        // Arrange
        var shellId = new ShellId("NewTenant");
        var newSettings = CreateShell("NewTenant", ["FeatureA"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(shellId, newSettings);

        var cache = new ShellSettingsCache();
        cache.Load([]); // empty cache

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadShellAsync(shellId);

        // Assert - new shell should be in the cache
        var cached = cache.GetById(shellId);
        Assert.NotNull(cached);
        Assert.Equal(["FeatureA"], cached.EnabledFeatures);
    }

    [Fact(DisplayName = "ReloadShellAsync with missing provider does not modify cache")]
    public async Task ReloadShellAsync_MissingFromProvider_DoesNotModifyCache()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var otherShellId = new ShellId("Tenant2");
        var tenant1Settings = CreateShell("Tenant1", ["FeatureA"]);
        var tenant2Settings = CreateShell("Tenant2", ["FeatureX"]);

        var provider = new StubShellSettingsProvider(); // returns null for any lookup
        var cache = new ShellSettingsCache();
        cache.Load([tenant1Settings, tenant2Settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act - ignore the expected exception
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ReloadShellAsync(shellId));

        // Assert - both shells should remain in cache
        Assert.NotNull(cache.GetById(shellId));
        Assert.NotNull(cache.GetById(otherShellId));
        Assert.Equal(2, cache.GetAll().Count);
    }

    #endregion

    #region US2 - Full Reload Reconciliation

    [Fact(DisplayName = "ReloadAllShellsAsync reconciles added shells")]
    public async Task ReloadAllShellsAsync_Reconciles_AddedShells()
    {
        // Arrange
        var existing = CreateShell("Tenant1", ["FeatureA"]);
        var newShell = CreateShell("NewTenant", ["FeatureX"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(new("Tenant1"), existing);
        provider.SetShell(new("NewTenant"), newShell);

        var cache = new ShellSettingsCache();
        cache.Load([existing]); // only Tenant1 initially

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert - NewTenant should now be in the cache
        Assert.NotNull(cache.GetById(new("NewTenant")));
        Assert.Equal(2, cache.GetAll().Count);
    }

    [Fact(DisplayName = "ReloadAllShellsAsync reconciles removed shells")]
    public async Task ReloadAllShellsAsync_Reconciles_RemovedShells()
    {
        // Arrange
        var tenant1 = CreateShell("Tenant1", ["FeatureA"]);
        var tenant2 = CreateShell("Tenant2", ["FeatureB"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(new("Tenant1"), tenant1); // only Tenant1 in provider

        var cache = new ShellSettingsCache();
        cache.Load([tenant1, tenant2]); // both in cache

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert - Tenant2 should no longer be in the cache
        Assert.Null(cache.GetById(new("Tenant2")));
        Assert.Single(cache.GetAll());
    }

    [Fact(DisplayName = "ReloadAllShellsAsync reconciles updated shell settings")]
    public async Task ReloadAllShellsAsync_Reconciles_UpdatedShells()
    {
        // Arrange
        var original = CreateShell("Tenant1", ["FeatureA"]);
        var updated = CreateShell("Tenant1", ["FeatureA", "FeatureB"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(new("Tenant1"), updated);

        var cache = new ShellSettingsCache();
        cache.Load([original]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert - cache should have the updated settings
        var cached = cache.GetById(new("Tenant1"));
        Assert.NotNull(cached);
        Assert.Contains("FeatureB", cached.EnabledFeatures);
    }

    [Fact(DisplayName = "ReloadAllShellsAsync publishes ShellsReloaded notification")]
    public async Task ReloadAllShellsAsync_PublishesShellsReloadedNotification()
    {
        // Arrange
        var settings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(new("Tenant1"), settings);

        var cache = new ShellSettingsCache();
        cache.Load([settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert - ShellsReloaded should have been published
        Assert.Contains(notifications.Notifications, n => n is ShellsReloaded);
    }

    #endregion

    #region US3 - Reload Notification Ordering and Scope

    [Fact(DisplayName = "ReloadShellAsync emits ShellReloading before ShellReloaded")]
    public async Task ReloadShellAsync_EmitsShellReloading_BeforeShellReloaded()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var settings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(shellId, settings);

        var cache = new ShellSettingsCache();
        cache.Load([settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadShellAsync(shellId);

        // Assert - ShellReloading should come first, ShellReloaded last
        var reloading = notifications.Notifications.OfType<ShellReloading>().ToList();
        var reloaded = notifications.Notifications.OfType<ShellReloaded>().ToList();
        Assert.Single(reloading);
        Assert.Single(reloaded);
        Assert.Equal(shellId, reloading[0].ShellId);
        Assert.Equal(shellId, reloaded[0].ShellId);

        var allNotifications = notifications.Notifications.ToList();
        var reloadingIndex = allNotifications.IndexOf(reloading[0]);
        var reloadedIndex = allNotifications.IndexOf(reloaded[0]);
        Assert.True(reloadingIndex < reloadedIndex, "ShellReloading must be emitted before ShellReloaded");
    }

    [Fact(DisplayName = "ReloadShellAsync failure does not emit ShellReloaded")]
    public async Task ReloadShellAsync_Failure_DoesNotEmitShellReloaded()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var settings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider(); // returns null
        var cache = new ShellSettingsCache();
        cache.Load([settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ReloadShellAsync(shellId));

        // Assert - ShellReloading may be emitted, but ShellReloaded must NOT be
        Assert.DoesNotContain(notifications.Notifications, n => n is ShellReloaded);
    }

    [Fact(DisplayName = "ReloadShellAsync ShellReloaded contains the reloaded shell in ChangedShells")]
    public async Task ReloadShellAsync_ShellReloaded_ContainsChangedShell()
    {
        // Arrange
        var shellId = new ShellId("Tenant1");
        var settings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(shellId, settings);

        var cache = new ShellSettingsCache();
        cache.Load([settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadShellAsync(shellId);

        // Assert
        var reloaded = notifications.Notifications.OfType<ShellReloaded>().Single();
        Assert.Contains(shellId, reloaded.ChangedShells);
    }

    [Fact(DisplayName = "ReloadAllShellsAsync emits aggregate ShellReloading and ShellReloaded")]
    public async Task ReloadAllShellsAsync_EmitsAggregateNotifications()
    {
        // Arrange
        var settings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(new("Tenant1"), settings);

        var cache = new ShellSettingsCache();
        cache.Load([settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert - aggregate notifications should have null ShellId
        var reloading = notifications.Notifications.OfType<ShellReloading>().Where(n => n.ShellId is null).ToList();
        var reloaded = notifications.Notifications.OfType<ShellReloaded>().Where(n => n.ShellId is null).ToList();
        Assert.Single(reloading);
        Assert.Single(reloaded);
    }

    [Fact(DisplayName = "ReloadAllShellsAsync emits ShellsReloaded before aggregate ShellReloaded")]
    public async Task ReloadAllShellsAsync_ShellsReloaded_BeforeAggregateShellReloaded()
    {
        // Arrange
        var settings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(new("Tenant1"), settings);

        var cache = new ShellSettingsCache();
        cache.Load([settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert - ShellsReloaded should come before the aggregate ShellReloaded
        var shellsReloaded = notifications.Notifications.OfType<ShellsReloaded>().Single();
        var aggregateReloaded = notifications.Notifications.OfType<ShellReloaded>().Single(n => n.ShellId is null);

        var allNotifications = notifications.Notifications.ToList();
        var shellsReloadedIndex = allNotifications.IndexOf(shellsReloaded);
        var aggregateReloadedIndex = allNotifications.IndexOf(aggregateReloaded);
        Assert.True(shellsReloadedIndex < aggregateReloadedIndex,
            "ShellsReloaded must be emitted before the aggregate ShellReloaded");
    }

    [Fact(DisplayName = "Existing lifecycle notifications are preserved during reload")]
    public async Task ReloadShellAsync_PreservesExistingLifecycleNotifications()
    {
        // Arrange - this test verifies that ShellReloading/ShellReloaded don't replace
        // other notification types.
        var shellId = new ShellId("Tenant1");
        var settings = CreateShell("Tenant1", ["FeatureA"]);

        var provider = new StubShellSettingsProvider();
        provider.SetShell(shellId, settings);

        var cache = new ShellSettingsCache();
        cache.Load([settings]);

        var notifications = new RecordingNotificationPublisher();
        var manager = CreateManager(cache, provider, notifications);

        // Act
        await manager.ReloadShellAsync(shellId);

        // Assert - ShellReloading and ShellReloaded are the reload-specific notifications
        // Other notifications (if emitted) should not be ShellReloading/ShellReloaded only
        var reloadNotifications = notifications.Notifications
            .Where(n => n is ShellReloading or ShellReloaded)
            .ToList();
        Assert.Equal(2, reloadNotifications.Count); // exactly one ShellReloading + one ShellReloaded
    }

    #endregion

    #region Helpers

    private static DefaultShellManager CreateManager(
        ShellSettingsCache cache,
        StubShellSettingsProvider provider,
        RecordingNotificationPublisher notifications)
    {
        var host = new StubShellHost();
        return new DefaultShellManager(host, cache, provider, notifications);
    }

    private static ShellSettings CreateShell(string id, string[] features) => new()
    {
        Id = new(id),
        EnabledFeatures = features
    };

    /// <summary>
    /// A minimal stub shell host for unit testing.
    /// </summary>
    private sealed class StubShellHost : IShellHost
    {
        public ShellContext DefaultShell => throw new NotImplementedException();
        public IReadOnlyCollection<ShellContext> AllShells => throw new NotImplementedException();
        public ShellContext GetShell(ShellId id) => throw new NotImplementedException();
    }

    /// <summary>
    /// A stub provider that returns configured shells by ID.
    /// </summary>
    internal sealed class StubShellSettingsProvider : IShellSettingsProvider
    {
        private readonly Dictionary<ShellId, ShellSettings> _shells = new();

        public void SetShell(ShellId id, ShellSettings settings) => _shells[id] = settings;

        public Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<ShellSettings>>(_shells.Values.ToList());

        public Task<ShellSettings?> GetShellSettingsAsync(ShellId shellId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_shells.GetValueOrDefault(shellId));
    }

    /// <summary>
    /// A notification publisher that records all published notifications.
    /// </summary>
    internal sealed class RecordingNotificationPublisher : INotificationPublisher
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

    #endregion
}
