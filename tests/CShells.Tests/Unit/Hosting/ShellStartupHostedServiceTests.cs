using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Hosting;
using CShells.Notifications;
using CShells.Tests.Integration.ShellHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Unit.Hosting;

/// <summary>
/// Tests for <see cref="ShellStartupHostedService"/>.
/// </summary>
public class ShellStartupHostedServiceTests
{
    [Fact(DisplayName = "StartAsync reconciles configured shells and publishes runtime status")]
    public async Task StartAsync_ReconcilesShells_ThenPublishesShellStatuses()
    {
        // Arrange
        var notifications = new RecordingNotificationPublisher();
        await using var provider = CreateServiceProvider(
            notifications,
            new ShellSettings(new("Default"), ["Core"]),
            new ShellSettings(new("Contoso"), ["Weather"]));
        var hostedService = GetStartupHostedService(provider);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        var shellsReloaded = Assert.Single(notifications.Notifications.OfType<ShellsReloaded>());
        Assert.Collection(
            shellsReloaded.Statuses.OrderBy(status => status.ShellId.Name, StringComparer.OrdinalIgnoreCase),
            status =>
            {
                Assert.Equal(new("Contoso"), status.ShellId);
                Assert.True(status.IsRoutable);
                Assert.True(status.IsInSync);
            },
            status =>
            {
                Assert.Equal(new("Default"), status.ShellId);
                Assert.True(status.IsRoutable);
                Assert.True(status.IsInSync);
            });
    }

    [Fact(DisplayName = "StartAsync is idempotent when called multiple times")]
    public async Task StartAsync_WhenCalledMultipleTimes_PublishesStartupNotificationsOnce()
    {
        // Arrange
        var notifications = new RecordingNotificationPublisher();
        await using var provider = CreateServiceProvider(notifications, new ShellSettings(new("Default"), ["Core"]));
        var hostedService = GetStartupHostedService(provider);

        // Act
        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Single(notifications.Notifications.OfType<ShellsReloaded>());
    }

    [Fact(DisplayName = "StopAsync is idempotent when called multiple times")]
    public async Task StopAsync_WhenCalledMultipleTimes_PublishesDeactivationOncePerShell()
    {
        // Arrange
        var notifications = new RecordingNotificationPublisher();
        await using var provider = CreateServiceProvider(
            notifications,
            new ShellSettings(new("Default"), ["Core"]),
            new ShellSettings(new("Contoso"), ["Weather"]));
        var hostedService = GetStartupHostedService(provider);
        await hostedService.StartAsync(CancellationToken.None);
        notifications.Notifications.Clear();

        // Act
        await hostedService.StopAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        // Assert
        var deactivating = notifications.Notifications.OfType<ShellDeactivating>().ToList();
        Assert.Equal(2, deactivating.Count);
        Assert.Contains(deactivating, notification => notification.Context.Id.Equals(new("Default")));
        Assert.Contains(deactivating, notification => notification.Context.Id.Equals(new("Contoso")));
    }

    private static ShellStartupHostedService GetStartupHostedService(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<ShellStartupHostedService>().Single();

    private static ServiceProvider CreateServiceProvider(
        RecordingNotificationPublisher notifications,
        params ShellSettings[] shells)
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationPublisher>(notifications);
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCShells(builder => builder
            .WithAssemblies(typeof(TestFixtures).Assembly)
            .WithProvider(new InMemoryShellSettingsProvider(shells)));
        var provider = services.BuildServiceProvider();
        var shellSettingsProvider = provider.GetRequiredService<IShellSettingsProvider>();
        var shellSettingsCache = provider.GetRequiredService<ShellSettingsCache>();
        shellSettingsCache.Load(shellSettingsProvider.GetShellSettingsAsync().GetAwaiter().GetResult().ToList());

        return provider;
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
