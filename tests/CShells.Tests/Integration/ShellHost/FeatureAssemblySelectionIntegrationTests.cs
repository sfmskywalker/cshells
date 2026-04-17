using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.ShellHost;

public class FeatureAssemblySelectionIntegrationTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    [Fact]
    public async Task DefaultHostDiscovery_MatchesFromHostAssemblies()
    {
        var defaultProvider = await BuildRootProviderAsync(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
        });
        var explicitHostProvider = await BuildRootProviderAsync(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
            DependencyInjection.CShellsBuilderExtensions.FromHostAssemblies(builder);
        });

        var defaultShell = GetShell(defaultProvider, new ShellId("Default"));
        var explicitHostShell = GetShell(explicitHostProvider, new ShellId("Default"));
        var defaultFeatures = defaultShell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();
        var explicitHostFeatures = explicitHostShell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();

        Assert.Equal(defaultFeatures.Select(feature => feature.Id), explicitHostFeatures.Select(feature => feature.Id));
        Assert.Contains(defaultFeatures, feature => feature.Id == "Core");
        Assert.Contains(defaultFeatures, feature => feature.Id == "Weather");
    }

    [Fact]
    public async Task ExplicitAssemblyMode_DoesNotImplicitlyIncludeHostAssemblies()
    {
        var serviceProvider = await BuildRootProviderAsync(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
            DependencyInjection.CShellsBuilderExtensions.FromAssemblies(builder, typeof(CShellsBuilder).Assembly);
        });

        // With partial activation, the shell activates but "Weather" is recorded as missing
        // because the CShellsBuilder assembly doesn't contain the Weather feature.
        var shellHost = serviceProvider.GetRequiredService<IShellHost>();
        var shell = shellHost.GetShell(new ShellId("Default"));
        Assert.DoesNotContain("Weather", shell.EnabledFeatures);
        Assert.Contains("Weather", shell.MissingFeatures);
    }

    [Fact]
    public async Task CustomAssemblyProvider_ComposesAdditivelyAndReceivesRootServiceProviderContext()
    {
        TrackingFeatureAssemblyProvider.CreatedProviders.Clear();
        var marker = new RootMarkerService();
        var serviceProvider = await BuildRootProviderAsync(
            builder =>
            {
                builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
                DependencyInjection.CShellsBuilderExtensions.FromAssemblies(builder, typeof(CShellsBuilder).Assembly);
                DependencyInjection.CShellsBuilderExtensions.WithAssemblyProvider(builder, sp =>
                {
                    var resolvedMarker = sp.GetRequiredService<RootMarkerService>();
                    return new TrackingFeatureAssemblyProvider([typeof(TestFixtures).Assembly], resolvedMarker);
                });
            },
            services => services.AddSingleton(marker));

        var shell = GetShell(serviceProvider, new ShellId("Default"));
        var features = shell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();

        Assert.Contains(TrackingFeatureAssemblyProvider.CreatedProviders, provider => ReferenceEquals(provider.ResolvedMarker, marker));
        Assert.Contains(features, feature => feature.Id == "Weather");
        Assert.Contains(features, feature => feature.Id == "Core");
    }

    [Fact]
    public async Task CustomAssemblyProvider_ComposesWithBuiltInProvidersUsingDeduplicatedDiscovery()
    {
        TrackingFeatureAssemblyProvider.CreatedProviders.Clear();
        var serviceProvider = await BuildRootProviderAsync(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
            DependencyInjection.CShellsBuilderExtensions.FromAssemblies(builder, typeof(TestFixtures).Assembly);
            DependencyInjection.CShellsBuilderExtensions.WithAssemblyProvider(builder, new TrackingFeatureAssemblyProvider([typeof(TestFixtures).Assembly]));
        });

        var shell = GetShell(serviceProvider, new ShellId("Default"));
        var features = shell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();

        Assert.Contains(features, feature => feature.Id == "Core");
        Assert.Contains(features, feature => feature.Id == "Weather");
        Assert.Single(features, feature => feature.Id == "Core");
        Assert.Single(features, feature => feature.Id == "Weather");
    }

    private async Task<ServiceProvider> BuildRootProviderAsync(Action<CShellsBuilder> configure, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        configureServices?.Invoke(services);
        services.AddCShells(configure);
        var serviceProvider = services.BuildServiceProvider();
        var shellSettingsProvider = serviceProvider.GetRequiredService<CShells.Configuration.IShellSettingsProvider>();
        var shellSettingsCache = serviceProvider.GetRequiredService<CShells.Configuration.ShellSettingsCache>();
        shellSettingsCache.Load((await shellSettingsProvider.GetShellSettingsAsync()).ToList());
        await serviceProvider.GetRequiredService<DefaultShellManager>().InitializeRuntimeAsync();
        _disposables.Add(serviceProvider);
        return serviceProvider;
    }

    private static ShellContext GetShell(IServiceProvider serviceProvider, ShellId shellId)
    {
        var shellHost = serviceProvider.GetRequiredService<IShellHost>();
        return shellHost.GetShell(shellId);
    }

    public async ValueTask DisposeAsync()
    {
        TrackingFeatureAssemblyProvider.CreatedProviders.Clear();

        foreach (var disposable in _disposables)
            await disposable.DisposeAsync();

        _disposables.Clear();
    }

    private sealed class RootMarkerService;

    private sealed class TrackingFeatureAssemblyProvider(IEnumerable<Assembly> assemblies, RootMarkerService? resolvedMarker = null) : IFeatureAssemblyProvider
    {
        public static List<TrackingFeatureAssemblyProvider> CreatedProviders { get; } = [];

        public RootMarkerService? ResolvedMarker { get; } = resolvedMarker;

        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            CreatedProviders.Add(this);
            return Task.FromResult(assemblies);
        }
    }
}
