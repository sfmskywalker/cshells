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
/// Tests for <see cref="DefaultShellHost"/> lifecycle operations (Dispose).
/// </summary>
[Collection(nameof(DefaultShellHostCollection))]
public class LifecycleTests(DefaultShellHostFixture fixture)
{
    [Fact(DisplayName = "Dispose disposes all service providers")]
    public async Task Dispose_DisposesServiceProviders()
    {
        // Arrange
        var host = fixture.CreateHost([new(new("TestShell"))], typeof(TestFixtures).Assembly);
        _ = host.GetShell(new("TestShell")); // Ensure the shell is built

        // Act
        await host.DisposeAsync();

        // Assert - After dispose, accessing shells should throw
        Assert.Throws<ObjectDisposedException>(() => host.DefaultShell);
        Assert.Throws<ObjectDisposedException>(() => host.GetShell(new("TestShell")));
        Assert.Throws<ObjectDisposedException>(() => _ = host.AllShells);
    }

    [Fact(DisplayName = "ReloadAllShellsAsync only swaps shells whose candidates commit while preserving other applied runtimes")]
    public async Task ReloadAllShellsAsync_MixedReconciliation_OnlyCommittedShellsAreReplaced()
    {
        // Arrange
        var alphaId = new ShellId("Alpha");
        var betaId = new ShellId("Beta");

        var alphaInitial = new ShellSettings(alphaId, ["Core"]);
        var betaInitial = new ShellSettings(betaId, ["Core"]);
        var alphaUpdated = new ShellSettings(alphaId, ["Weather"]);
        var betaDeferred = new ShellSettings(betaId, ["Core", "MissingFeature"]);

        var cache = fixture.CreateCache([alphaInitial, betaInitial]);
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
        var runtimeAccessor = new ShellRuntimeStateAccessor(stateStore);
        var manager = new DefaultShellManager(
            host,
            cache,
            new MutableInMemoryShellSettingsProvider([alphaUpdated, betaDeferred]),
            stateStore,
            runtimeCatalog,
            runtimeAccessor,
            notifications,
            NullLogger<DefaultShellManager>.Instance);

        await manager.InitializeRuntimeAsync();
        var alphaBefore = host.GetShell(alphaId);
        var betaBefore = host.GetShell(betaId);
        notifications.Notifications.Clear();

        // Act
        await manager.ReloadAllShellsAsync();

        // Assert
        var alphaAfter = host.GetShell(alphaId);
        var betaAfter = host.GetShell(betaId);
        Assert.NotSame(alphaBefore, alphaAfter);
        Assert.Same(betaBefore, betaAfter);

        var statuses = runtimeAccessor.GetAllShells().OrderBy(status => status.ShellId.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Collection(
            statuses,
            alphaStatus =>
            {
                Assert.Equal(alphaId, alphaStatus.ShellId);
                Assert.True(alphaStatus.IsInSync);
                Assert.True(alphaStatus.IsRoutable);
                Assert.Equal(2, alphaStatus.AppliedGeneration);
                Assert.Equal(ShellReconciliationOutcome.Active, alphaStatus.Outcome);
            },
            status =>
            {
                Assert.Equal(betaId, status.ShellId);
                Assert.False(status.IsInSync);
                Assert.True(status.IsRoutable);
                Assert.Equal(2, status.DesiredGeneration);
                Assert.Equal(1, status.AppliedGeneration);
                Assert.Equal(ShellReconciliationOutcome.Active, status.Outcome);
                Assert.Contains("MissingFeature", status.BlockingReason);
                Assert.Equal(["MissingFeature"], status.MissingFeatures);
            });

        Assert.Contains(notifications.Notifications, notification => notification is ShellDeactivating deactivating && deactivating.Context.Id == alphaId);
        Assert.Contains(notifications.Notifications, notification => notification is ShellActivated activated && activated.Context.Id == alphaId);
        Assert.DoesNotContain(notifications.Notifications, notification => notification is ShellDeactivating deactivating && deactivating.Context.Id == betaId);
        Assert.DoesNotContain(notifications.Notifications, notification => notification is ShellActivated activated && activated.Context.Id == betaId);
    }

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
