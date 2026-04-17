using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;
using CShells.Resolution;
using CShells.Tests.Integration.ShellHost;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Integration.ShellHost;

public class DefaultShellFallbackIntegrationTests
{
    [Fact(DisplayName = "Explicit Default shell with missing features still resolves because it activates with partial features")]
    public async Task ExplicitDefault_WithMissingFeatures_StillResolves()
    {
        // Arrange
        var defaultSettings = new ShellSettings(new ShellId("Default"), ["MissingFeature"]);
        var otherSettings = new ShellSettings(new ShellId("Contoso"), ["Core"]);
        var cache = new ShellSettingsCache();
        cache.Load([defaultSettings, otherSettings]);

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
        var manager = new DefaultShellManager(host, cache, new MutableInMemoryShellSettingsProvider([defaultSettings, otherSettings]), stateStore, runtimeCatalog, runtimeAccessor, notifications, NullLogger<DefaultShellManager>.Instance);
        var resolver = new DefaultShellResolverStrategy(host, runtimeAccessor);

        await manager.InitializeRuntimeAsync();

        // Act
        var resolved = resolver.Resolve(new ShellResolutionContext());

        // Assert — Default activates with missing features and is routable
        Assert.Equal(new ShellId("Default"), resolved);
        Assert.Equal(2, host.AllShells.Count);

        var defaultStatus = runtimeAccessor.GetShell(new ShellId("Default"));
        Assert.NotNull(defaultStatus);
        Assert.Equal(ShellReconciliationOutcome.ActiveWithMissingFeatures, defaultStatus!.Outcome);
        Assert.True(defaultStatus.IsRoutable);
        Assert.Contains("MissingFeature", defaultStatus.MissingFeatures);
    }

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public Task PublishAsync<TNotification>(
            TNotification notification,
            INotificationStrategy? strategy = null,
            CancellationToken cancellationToken = default) where TNotification : class, INotification => Task.CompletedTask;
    }
}
