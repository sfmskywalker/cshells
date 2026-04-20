using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;
using CShells.Tests.Integration.ShellHost;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Unit.Management;

public class DefaultShellManagerReloadTests
{
    [Fact(DisplayName = "ReloadShellAsync rebuilds shell with available features when some are missing")]
    public async Task ReloadShellAsync_MissingFeatures_RebuildsWithAvailableFeatures()
    {
        // Arrange
        var shellId = new ShellId("Contoso");
        var initialSettings = new ShellSettings(shellId, ["Core"]);
        var partialSettings = new ShellSettings(shellId, ["Core", "MissingFeature"]);
        var provider = new MutableInMemoryShellSettingsProvider([partialSettings]);
        var notifications = new RecordingNotificationPublisher();
        var runtime = CreateRuntime([initialSettings], provider, notifications);

        await runtime.Manager.InitializeRuntimeAsync();
        notifications.Notifications.Clear();

        // Act
        await runtime.Manager.ReloadShellAsync(shellId);

        // Assert — shell rebuilds with available features; missing features recorded
        var appliedContext = runtime.Host.GetShell(shellId);
        Assert.Contains("Core", appliedContext.EnabledFeatures);
        Assert.DoesNotContain("MissingFeature", appliedContext.EnabledFeatures);
        Assert.Equal(["MissingFeature"], appliedContext.MissingFeatures);

        var status = runtime.Accessor.GetShell(shellId);
        Assert.NotNull(status);
        Assert.Equal(2, status!.DesiredGeneration);
        Assert.Equal(2, status.AppliedGeneration);
        Assert.Equal(ShellReconciliationOutcome.ActiveWithMissingFeatures, status.Outcome);
        Assert.True(status.IsInSync);
        Assert.True(status.IsRoutable);
        Assert.Equal(["MissingFeature"], status.MissingFeatures);

        // Lifecycle notifications fire (shell was deactivated then reactivated)
        Assert.Contains(notifications.Notifications, notification => notification is ShellActivated);
        Assert.Contains(notifications.Notifications, notification => notification is ShellDeactivating);
        Assert.Contains(notifications.Notifications, notification => notification is ShellReloaded reloaded && reloaded.ShellId == shellId);
    }

    [Fact(DisplayName = "ReloadShellAsync with missing provider result throws and preserves the applied runtime")]
    public async Task ReloadShellAsync_MissingProviderResult_ThrowsAndPreservesAppliedRuntime()
    {
        // Arrange
        var shellId = new ShellId("Contoso");
        var initialSettings = new ShellSettings(shellId, ["Core"]);
        var provider = new MutableInMemoryShellSettingsProvider();
        var notifications = new RecordingNotificationPublisher();
        var runtime = CreateRuntime([initialSettings], provider, notifications);

        await runtime.Manager.InitializeRuntimeAsync();
        var originalContext = runtime.Host.GetShell(shellId);
        notifications.Notifications.Clear();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Manager.ReloadShellAsync(shellId));
        var preservedContext = runtime.Host.GetShell(shellId);
        Assert.Same(originalContext, preservedContext);
        Assert.DoesNotContain(notifications.Notifications, notification => notification is ShellReloaded);
    }

    [Fact(DisplayName = "ReloadAllShellsAsync commits a ready successor and publishes applied-runtime lifecycle notifications")]
    public async Task ReloadAllShellsAsync_ReadyCandidate_CommitsAndPublishesLifecycleNotifications()
    {
        // Arrange
        var shellId = new ShellId("Contoso");
        var initialSettings = new ShellSettings(shellId, ["Core"]);
        var updatedSettings = new ShellSettings(shellId, ["Weather"]);
        var provider = new MutableInMemoryShellSettingsProvider([updatedSettings]);
        var notifications = new RecordingNotificationPublisher();
        var runtime = CreateRuntime([initialSettings], provider, notifications);

        await runtime.Manager.InitializeRuntimeAsync();
        var previousContext = runtime.Host.GetShell(shellId);
        notifications.Notifications.Clear();

        // Act
        await runtime.Manager.ReloadAllShellsAsync();

        // Assert
        var currentContext = runtime.Host.GetShell(shellId);
        Assert.NotSame(previousContext, currentContext);
        Assert.Contains("Weather", currentContext.EnabledFeatures);

        Assert.Contains(notifications.Notifications, notification => notification is ShellDeactivating deactivating && deactivating.Context.Id == shellId);
        Assert.Contains(notifications.Notifications, notification => notification is ShellActivated activated && activated.Context.Id == shellId);

        var aggregateReload = Assert.Single(notifications.Notifications.OfType<ShellReloaded>(), notification => notification.ShellId is null);
        Assert.Contains(shellId, aggregateReload.ChangedShells);
        Assert.Contains(aggregateReload.Statuses, status => status.ShellId == shellId && status.IsInSync && status.IsRoutable);
    }

    private static TestRuntime CreateRuntime(
        IEnumerable<ShellSettings> initialShells,
        IShellSettingsProvider provider,
        RecordingNotificationPublisher notifications)
    {
        var cache = new ShellSettingsCache();
        cache.Load(initialShells.ToList());

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
            notificationPublisher: notifications,
            logger: NullLogger<DefaultShellHost>.Instance);
        var accessorService = new ShellRuntimeStateAccessor(stateStore);
        var manager = new DefaultShellManager(host, cache, provider, stateStore, runtimeCatalog, accessorService, notifications, NullLogger<DefaultShellManager>.Instance);

        return new(cache, host, manager, accessorService);
    }

    private sealed record TestRuntime(
        ShellSettingsCache Cache,
        DefaultShellHost Host,
        DefaultShellManager Manager,
        IShellRuntimeStateAccessor Accessor);

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public List<object> Notifications { get; } = [];

        public Task PublishAsync<TNotification>(
            TNotification notification,
            INotificationStrategy? strategy = null,
            CancellationToken cancellationToken = default) where TNotification : class, INotification
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
