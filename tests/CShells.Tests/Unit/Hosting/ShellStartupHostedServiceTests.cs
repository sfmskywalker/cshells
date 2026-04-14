using CShells.Hosting;
using CShells.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Unit.Hosting;

/// <summary>
/// Tests for <see cref="ShellStartupHostedService"/>.
/// </summary>
public class ShellStartupHostedServiceTests
{
    [Fact(DisplayName = "StartAsync activates each shell once and then publishes ShellsReloaded")]
    public async Task StartAsync_ActivatesShells_ThenPublishesShellsReloaded()
    {
        // Arrange
        var defaultShell = CreateShellContext(new ShellSettings(new("Default"), ["Core"]));
        var contosoShell = CreateShellContext(new ShellSettings(new("Contoso"), ["Posts"]));
        var shellHost = new StubShellHost([defaultShell, contosoShell]);
        var notifications = new RecordingNotificationPublisher();
        var hostedService = new ShellStartupHostedService(
            shellHost,
            shellHost,
            notifications,
            NullLogger<ShellStartupHostedService>.Instance);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, shellHost.EnsureInitializedCalls);
        Assert.Collection(
            notifications.Notifications,
            notification => Assert.Equal(defaultShell, Assert.IsType<ShellActivated>(notification).Context),
            notification => Assert.Equal(contosoShell, Assert.IsType<ShellActivated>(notification).Context),
            notification =>
            {
                var shellsReloaded = Assert.IsType<ShellsReloaded>(notification);
                Assert.Collection(
                    shellsReloaded.AllShells,
                    shell => Assert.Equal(defaultShell.Settings.Id, shell.Id),
                    shell => Assert.Equal(contosoShell.Settings.Id, shell.Id));
            });
    }

    [Fact(DisplayName = "StartAsync is idempotent when called multiple times")]
    public async Task StartAsync_WhenCalledMultipleTimes_PublishesStartupNotificationsOnce()
    {
        // Arrange
        var defaultShell = CreateShellContext(new ShellSettings(new("Default"), ["Core"]));
        var shellHost = new StubShellHost([defaultShell]);
        var notifications = new RecordingNotificationPublisher();
        var hostedService = new ShellStartupHostedService(
            shellHost,
            shellHost,
            notifications,
            NullLogger<ShellStartupHostedService>.Instance);

        // Act
        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, shellHost.EnsureInitializedCalls);
        Assert.Single(notifications.Notifications.OfType<ShellActivated>());
        Assert.Single(notifications.Notifications.OfType<ShellsReloaded>());
    }

    [Fact(DisplayName = "StopAsync is idempotent when called multiple times")]
    public async Task StopAsync_WhenCalledMultipleTimes_PublishesDeactivationOncePerShell()
    {
        // Arrange
        var defaultShell = CreateShellContext(new ShellSettings(new("Default"), ["Core"]));
        var contosoShell = CreateShellContext(new ShellSettings(new("Contoso"), ["Posts"]));
        var shellHost = new StubShellHost([defaultShell, contosoShell]);
        var notifications = new RecordingNotificationPublisher();
        var hostedService = new ShellStartupHostedService(
            shellHost,
            shellHost,
            notifications,
            NullLogger<ShellStartupHostedService>.Instance);

        // Act
        await hostedService.StopAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        // Assert
        var deactivating = notifications.Notifications.OfType<ShellDeactivating>().ToList();
        Assert.Equal(2, deactivating.Count);
        Assert.Equal(defaultShell, deactivating[0].Context);
        Assert.Equal(contosoShell, deactivating[1].Context);
    }

    private static ShellContext CreateShellContext(ShellSettings settings) =>
        new(settings, new ServiceCollection().BuildServiceProvider(), settings.EnabledFeatures.ToList().AsReadOnly());

    private sealed class StubShellHost(IReadOnlyCollection<ShellContext> shells) : IShellHost, IShellHostInitializer
    {
        private IReadOnlyCollection<ShellContext> Shells { get; } = shells;

        public int EnsureInitializedCalls { get; private set; }

        public ShellContext DefaultShell => Shells.First();

        public IReadOnlyCollection<ShellContext> AllShells => Shells;

        public ShellContext GetShell(ShellId id) =>
            Shells.FirstOrDefault(shell => shell.Id.Equals(id))
            ?? throw new KeyNotFoundException($"Shell '{id}' not found.");

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitializedCalls++;
            return Task.CompletedTask;
        }

        public ValueTask EvictShellAsync(ShellId shellId) => ValueTask.CompletedTask;

        public ValueTask EvictAllShellsAsync() => ValueTask.CompletedTask;
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

