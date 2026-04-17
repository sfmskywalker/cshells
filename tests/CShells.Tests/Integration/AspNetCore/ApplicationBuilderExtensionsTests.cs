using System.Reflection;
using CShells.AspNetCore.Features;
using CShells.AspNetCore.Notifications;
using CShells.AspNetCore.Routing;
using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using CShells.Notifications;
using CShells.Resolution;
using CShells.Tests.Integration.ShellHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Tests for <see cref="ApplicationBuilderExtensions"/>.
/// </summary>
public class ApplicationBuilderExtensionsTests
{
    public static IEnumerable<object[]> GuardClauseData()
    {
        yield return new object[] { null!, "app" };
    }

    [Theory(DisplayName = "MapCShells guard clauses throw ArgumentNullException")]
    [MemberData(nameof(GuardClauseData))]
    public void MapCShells_GuardClauses_ThrowArgumentNullException(IApplicationBuilder? app, string expectedParam)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CShells.AspNetCore.Extensions.ApplicationBuilderExtensions.MapShells(app!));
        Assert.Equal(expectedParam, exception.ParamName);
    }

    [Fact(DisplayName = "MapCShells configures middleware and endpoints")]
    public void MapCShells_ConfiguresMiddlewareAndEndpoints()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IShellResolver, NullShellResolver>();
        services.AddSingleton<IShellFeatureFactory, DefaultShellFeatureFactory>();
        services.AddSingleton<IShellHost, EmptyShellHost>();
        services.AddSingleton<EndpointRouteBuilderAccessor>();
        services.AddSingleton<DynamicShellEndpointDataSource>();
        services.AddSingleton<ApplicationBuilderAccessor>();
        var serviceProvider = services.BuildServiceProvider();
        var app = new TestApplicationBuilder(serviceProvider);

        // Act
        var result = CShells.AspNetCore.Extensions.ApplicationBuilderExtensions.MapShells(app);

        // Assert
        Assert.NotNull(result);
    }

    [Fact(DisplayName = "Shell endpoint registration exposes endpoints for all applied runtimes including those with missing features")]
    public async Task ShellEndpointRegistration_AllAppliedShellsExposeEndpoints()
    {
        // Arrange
        var appliedShell = CreateShell("Applied", "applied", ["TestWeb"]);
        var partialShell = CreateShell("Partial", "partial", ["TestWeb", "MissingFeature"]);
        var cache = new ShellSettingsCache();
        cache.Load([appliedShell, partialShell]);

        var (rootServices, rootProvider) = TestFixtures.CreateRootServices();
        var rootAccessor = TestFixtures.CreateRootServicesAccessor(rootServices);
        var featureFactory = new DefaultShellFeatureFactory(rootProvider);
        var stateStore = new ShellRuntimeStateStore();
        var notifications = new RecordingNotificationPublisher();
        var runtimeCatalog = new RuntimeFeatureCatalog(
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(ApplicationBuilderExtensionsTests).Assembly]),
            NullLogger<RuntimeFeatureCatalog>.Instance);
        await using var host = new Hosting.DefaultShellHost(
            cache,
            _ => Task.FromResult<IReadOnlyCollection<Assembly>>([typeof(ApplicationBuilderExtensionsTests).Assembly]),
            rootProvider,
            rootAccessor,
            featureFactory,
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
            new MutableInMemoryShellSettingsProvider([appliedShell, partialShell]),
            stateStore,
            runtimeCatalog,
            runtimeAccessor,
            notifications,
            NullLogger<DefaultShellManager>.Instance);

        await manager.InitializeRuntimeAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IShellResolver, NullShellResolver>();
        services.AddSingleton<IShellFeatureFactory>(featureFactory);
        services.AddSingleton<IShellHost>(host);
        services.AddSingleton<EndpointRouteBuilderAccessor>();
        services.AddSingleton<DynamicShellEndpointDataSource>();
        services.AddSingleton<ApplicationBuilderAccessor>();
        var serviceProvider = services.BuildServiceProvider();
        var app = new TestApplicationBuilder(serviceProvider);

        _ = CShells.AspNetCore.Extensions.ApplicationBuilderExtensions.MapShells(app);

        var dataSource = serviceProvider.GetRequiredService<DynamicShellEndpointDataSource>();
        var handler = new ShellEndpointRegistrationHandler(
            dataSource,
            featureFactory,
            host,
            serviceProvider.GetRequiredService<EndpointRouteBuilderAccessor>(),
            serviceProvider.GetRequiredService<ApplicationBuilderAccessor>());

        // Act — both shells are applied (partial shell activated with available features)
        var appliedStatuses = runtimeAccessor.GetAllShells().Where(s => s.IsRoutable);
        foreach (var status in appliedStatuses)
            await handler.HandleAsync(new ShellActivated(host.GetShell(status.ShellId)));

        // Assert — both shells expose endpoints for their loaded features
        var routedEndpoints = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => (
                endpoint.Metadata.GetMetadata<ShellEndpointMetadata>()?.ShellId,
                Pattern: endpoint.RoutePattern.RawText))
            .ToList();

        Assert.Contains(routedEndpoints, endpoint => endpoint.ShellId == new ShellId("Applied") && endpoint.Pattern == "/applied/ping");
        Assert.Contains(routedEndpoints, endpoint => endpoint.ShellId == new ShellId("Partial") && endpoint.Pattern == "/partial/ping");
    }

    // Test helpers
    private class NullShellResolver : IShellResolver
    {
        public ShellId? Resolve(ShellResolutionContext context) => null;
    }

    private class EmptyShellHost : IShellHost
    {
        public ShellContext DefaultShell => throw new InvalidOperationException();
        public IReadOnlyCollection<ShellContext> AllShells => [];
        public ShellContext GetShell(ShellId id) => throw new KeyNotFoundException();
        public ValueTask EvictShellAsync(ShellId shellId) => ValueTask.CompletedTask;
        public ValueTask EvictAllShellsAsync() => ValueTask.CompletedTask;
    }

    [ShellFeature("TestWeb")]
    public sealed class TestWebFeature : IWebShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
        {
            endpoints.MapGet("/ping", () => Results.Ok(new { ok = true }));
        }
    }

    private static ShellSettings CreateShell(string name, string path, IReadOnlyCollection<string> features)
    {
        return new ShellSettings
        {
            Id = new(name),
            EnabledFeatures = [.. features],
            ConfigurationData = new Dictionary<string, object>
            {
                ["WebRouting:Path"] = path
            }
        };
    }

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public Task PublishAsync<TNotification>(
            TNotification notification,
            INotificationStrategy? strategy = null,
            CancellationToken cancellationToken = default) where TNotification : class, INotification => Task.CompletedTask;
    }

    private class TestApplicationBuilder(IServiceProvider serviceProvider) : IApplicationBuilder, IEndpointRouteBuilder
    {
        private readonly List<Func<RequestDelegate, RequestDelegate>> _components = [];
        private readonly List<EndpointDataSource> _dataSources = [];

        public IServiceProvider ApplicationServices { get; set; } = serviceProvider;
        public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
        public IFeatureCollection ServerFeatures => throw new NotImplementedException();

        // IEndpointRouteBuilder implementation
        public IServiceProvider ServiceProvider => ApplicationServices;
        public ICollection<EndpointDataSource> DataSources => _dataSources;

        public RequestDelegate Build()
        {
            RequestDelegate app = _ => Task.CompletedTask;
            for (var i = _components.Count - 1; i >= 0; i--)
            {
                app = _components[i](app);
            }
            return app;
        }

        public IApplicationBuilder New() => new TestApplicationBuilder(ApplicationServices);

        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
        {
            _components.Add(middleware);
            return this;
        }

        public IApplicationBuilder CreateApplicationBuilder() => new TestApplicationBuilder(ApplicationServices);
    }
}
