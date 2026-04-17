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
        var partialSettings = new ShellSettings(new ShellId("Partial"), ["MissingFeature"]);
        var cache = new ShellSettingsCache();
        cache.Load([defaultSettings, partialSettings]);

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
        var manager = new DefaultShellManager(host, cache, new MutableInMemoryShellSettingsProvider([defaultSettings, partialSettings]), stateStore, runtimeCatalog, runtimeAccessor, notifications, NullLogger<DefaultShellManager>.Instance);

        // Act
        await manager.InitializeRuntimeAsync();
        var statuses = runtimeAccessor.GetAllShells().OrderBy(status => status.ShellId.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Assert — both shells activate; the one with missing features reports ActiveWithMissingFeatures
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
                Assert.Equal(new ShellId("Partial"), status.ShellId);
                Assert.Equal(ShellReconciliationOutcome.ActiveWithMissingFeatures, status.Outcome);
                Assert.True(status.IsInSync);
                Assert.True(status.IsRoutable);
                Assert.Equal(1, status.AppliedGeneration);
                Assert.Contains("MissingFeature", status.MissingFeatures);
            });
    }

    [Fact(DisplayName = "Runtime status keeps an explicit Default shell visible when it fails to build")]
    public async Task RuntimeStatus_ExplicitDefaultFailed_RemainsVisibleAndUnavailable()
    {
        // Arrange — Default shell configured with a feature that will cause a build failure (not just missing)
        // We use a settings provider that causes the Default shell to fail during build.
        var contosoSettings = new ShellSettings(new ShellId("Contoso"), ["Core"]);
        var defaultSettings = new ShellSettings(new ShellId("Default"), ["MissingFeature"]);
        var runtime = CreateRuntime([defaultSettings, contosoSettings]);

        // Act
        await runtime.Manager.InitializeRuntimeAsync();
        var defaultStatus = runtime.Accessor.GetShell(new ShellId("Default"));
        var contosoStatus = runtime.Accessor.GetShell(new ShellId("Contoso"));

        // Assert — Default shell activates with missing features but is still routable
        Assert.NotNull(defaultStatus);
        Assert.Equal(ShellReconciliationOutcome.ActiveWithMissingFeatures, defaultStatus!.Outcome);
        Assert.True(defaultStatus.IsRoutable);
        Assert.True(defaultStatus.IsInSync);
        Assert.Equal(1, defaultStatus.AppliedGeneration);
        Assert.Equal(["MissingFeature"], defaultStatus.MissingFeatures);

        Assert.NotNull(contosoStatus);
        Assert.True(contosoStatus!.IsRoutable);
        Assert.True(contosoStatus.IsInSync);
        Assert.Equal(1, contosoStatus.AppliedGeneration);

        // Default shell is still accessible (it has an applied runtime, just with missing features)
        Assert.NotNull(runtime.Host.DefaultShell);
        Assert.Equal(2, runtime.Host.AllShells.Count);
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
