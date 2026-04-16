using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Integration.ShellHost;

public class ShellRuntimeStatusIntegrationTests
{
    [Fact(DisplayName = "Runtime status exposes mixed desired and applied shell states after reconciliation")]
    public async Task RuntimeStatus_ExposesMixedDesiredAndAppliedStates()
    {
        // Arrange
        var defaultSettings = new ShellSettings(new ShellId("Default"), ["Core"]);
        var deferredSettings = new ShellSettings(new ShellId("Deferred"), ["MissingFeature"]);
        var cache = new ShellSettingsCache();
        cache.Load([defaultSettings, deferredSettings]);

        var (services, rootProvider) = TestFixtures.CreateRootServices();
        var accessor = TestFixtures.CreateRootServicesAccessor(services);
        var featureFactory = new DefaultShellFeatureFactory(rootProvider);
        var exclusionRegistry = new ShellServiceExclusionRegistry([]);
        var notifications = new RecordingNotificationPublisher();
        var stateStore = new ShellRuntimeStateStore();
        var runtimeCatalog = new RuntimeFeatureCatalog(
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(TestFixtures).Assembly]),
            NullLogger<RuntimeFeatureCatalog>.Instance);
        var host = new Hosting.DefaultShellHost(
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
            logger: NullLogger<Hosting.DefaultShellHost>.Instance);
        var runtimeAccessor = new ShellRuntimeStateAccessor(stateStore);
        var manager = new DefaultShellManager(host, cache, new MutableInMemoryShellSettingsProvider([defaultSettings, deferredSettings]), stateStore, runtimeCatalog, runtimeAccessor, notifications, NullLogger<DefaultShellManager>.Instance);

        // Act
        await manager.InitializeRuntimeAsync();
        var statuses = runtimeAccessor.GetAllShells().OrderBy(status => status.ShellId.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Assert
        Assert.Collection(
            statuses,
            status =>
            {
                Assert.Equal(new ShellId("Default"), status.ShellId);
                Assert.Equal(ShellReconciliationOutcome.Active, status.Outcome);
                Assert.True(status.IsInSync);
                Assert.True(status.IsRoutable);
                Assert.Equal(1, status.AppliedGeneration);
            },
            status =>
            {
                Assert.Equal(new ShellId("Deferred"), status.ShellId);
                Assert.Equal(ShellReconciliationOutcome.DeferredDueToMissingFeatures, status.Outcome);
                Assert.False(status.IsInSync);
                Assert.False(status.IsRoutable);
                Assert.Null(status.AppliedGeneration);
                Assert.Contains("MissingFeature", status.BlockingReason);
                Assert.Contains("MissingFeature", status.MissingFeatures);
            });
    }

    [Fact(DisplayName = "Runtime status keeps an explicit Default shell visible as unapplied without falling back to another applied shell")]
    public async Task RuntimeStatus_ExplicitDefaultUnapplied_RemainsVisibleAndUnavailable()
    {
        // Arrange
        var defaultSettings = new ShellSettings(new ShellId("Default"), ["MissingFeature"]);
        var contosoSettings = new ShellSettings(new ShellId("Contoso"), ["Core"]);
        var runtime = CreateRuntime([defaultSettings, contosoSettings]);

        // Act
        await runtime.Manager.InitializeRuntimeAsync();
        var defaultStatus = runtime.Accessor.GetShell(new ShellId("Default"));
        var contosoStatus = runtime.Accessor.GetShell(new ShellId("Contoso"));

        // Assert
        Assert.NotNull(defaultStatus);
        Assert.Equal(ShellReconciliationOutcome.DeferredDueToMissingFeatures, defaultStatus!.Outcome);
        Assert.False(defaultStatus.IsRoutable);
        Assert.False(defaultStatus.IsInSync);
        Assert.Null(defaultStatus.AppliedGeneration);
        Assert.Equal(["MissingFeature"], defaultStatus.MissingFeatures);

        Assert.NotNull(contosoStatus);
        Assert.True(contosoStatus!.IsRoutable);
        Assert.True(contosoStatus.IsInSync);
        Assert.Equal(1, contosoStatus.AppliedGeneration);

        Assert.Throws<KeyNotFoundException>(() => runtime.Host.DefaultShell);
        Assert.Equal(new ShellId("Contoso"), Assert.Single(runtime.Host.AllShells).Id);
    }

    private static TestRuntime CreateRuntime(IEnumerable<ShellSettings> settings)
    {
        var cache = new ShellSettingsCache();
        var shells = settings.ToList();
        cache.Load(shells);

        var (services, rootProvider) = TestFixtures.CreateRootServices();
        var accessor = TestFixtures.CreateRootServicesAccessor(services);
        var featureFactory = new DefaultShellFeatureFactory(rootProvider);
        var exclusionRegistry = new ShellServiceExclusionRegistry([]);
        var notifications = new RecordingNotificationPublisher();
        var stateStore = new ShellRuntimeStateStore();
        var runtimeCatalog = new RuntimeFeatureCatalog(
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(TestFixtures).Assembly]),
            NullLogger<RuntimeFeatureCatalog>.Instance);
        var host = new Hosting.DefaultShellHost(
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
            logger: NullLogger<Hosting.DefaultShellHost>.Instance);
        var runtimeAccessor = new ShellRuntimeStateAccessor(stateStore);
        var manager = new DefaultShellManager(host, cache, new MutableInMemoryShellSettingsProvider(shells), stateStore, runtimeCatalog, runtimeAccessor, notifications, NullLogger<DefaultShellManager>.Instance);

        return new(host, manager, runtimeAccessor);
    }

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public Task PublishAsync<TNotification>(
            TNotification notification,
            INotificationStrategy? strategy = null,
            CancellationToken cancellationToken = default) where TNotification : class, INotification => Task.CompletedTask;
    }

    private sealed record TestRuntime(
        Hosting.DefaultShellHost Host,
        DefaultShellManager Manager,
        IShellRuntimeStateAccessor Accessor);
}

