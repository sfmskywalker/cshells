using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.ShellHost;

public class FeatureAssemblySelectionIntegrationTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    [Fact]
    public void DefaultHostDiscovery_MatchesFromHostAssemblies()
    {
        var defaultProvider = BuildRootProvider(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
        });
        var explicitHostProvider = BuildRootProvider(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
            CShellsBuilderExtensions.FromHostAssemblies(builder);
        });

        var defaultShell = GetShell(defaultProvider, new("Default"));
        var explicitHostShell = GetShell(explicitHostProvider, new("Default"));
        var defaultFeatures = defaultShell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();
        var explicitHostFeatures = explicitHostShell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();

        Assert.Equal(defaultFeatures.Select(feature => feature.Id), explicitHostFeatures.Select(feature => feature.Id));
        Assert.Contains(defaultFeatures, feature => feature.Id == "Core");
        Assert.Contains(defaultFeatures, feature => feature.Id == "Weather");
    }

    [Fact]
    public void ExplicitAssemblyMode_DoesNotImplicitlyIncludeHostAssemblies()
    {
        var serviceProvider = BuildRootProvider(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
            CShellsBuilderExtensions.FromAssemblies(builder, typeof(CShellsBuilder).Assembly);
        });

        var shellHost = serviceProvider.GetRequiredService<IShellHost>();
        var exception = Assert.Throws<InvalidOperationException>(() => shellHost.GetShell(new("Default")));

        Assert.Contains("Weather", exception.Message);
    }

    [Fact]
    public void CustomAssemblyProvider_ComposesAdditivelyAndReceivesRootServiceProviderContext()
    {
        TrackingFeatureAssemblyProvider.CreatedProviders.Clear();
        var marker = new RootMarkerService();
        var serviceProvider = BuildRootProvider(
            builder =>
            {
                builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
                CShellsBuilderExtensions.FromAssemblies(builder, typeof(CShellsBuilder).Assembly);
                CShellsBuilderExtensions.WithAssemblyProvider(builder, sp =>
                {
                    var resolvedMarker = sp.GetRequiredService<RootMarkerService>();
                    return new TrackingFeatureAssemblyProvider([typeof(TestFixtures).Assembly], resolvedMarker);
                });
            },
            services => services.AddSingleton(marker));

        var shell = GetShell(serviceProvider, new("Default"));
        var features = shell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();

        Assert.Contains(TrackingFeatureAssemblyProvider.CreatedProviders, provider => ReferenceEquals(provider.ResolvedMarker, marker));
        Assert.Contains(features, feature => feature.Id == "Weather");
        Assert.Contains(features, feature => feature.Id == "Core");
    }

    [Fact]
    public void CustomAssemblyProvider_ComposesWithBuiltInProvidersUsingDeduplicatedDiscovery()
    {
        TrackingFeatureAssemblyProvider.CreatedProviders.Clear();
        var serviceProvider = BuildRootProvider(builder =>
        {
            builder.AddShell("Default", shell => shell.WithFeatures("Weather"));
            CShellsBuilderExtensions.FromAssemblies(builder, typeof(TestFixtures).Assembly);
            CShellsBuilderExtensions.WithAssemblyProvider(builder, new TrackingFeatureAssemblyProvider([typeof(TestFixtures).Assembly]));
        });

        var shell = GetShell(serviceProvider, new("Default"));
        var features = shell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();

        Assert.Contains(features, feature => feature.Id == "Core");
        Assert.Contains(features, feature => feature.Id == "Weather");
        Assert.Single(features, feature => feature.Id == "Core");
        Assert.Single(features, feature => feature.Id == "Weather");
    }

    private ServiceProvider BuildRootProvider(Action<CShellsBuilder> configure, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        configureServices?.Invoke(services);
        services.AddCShells(configure);
        var serviceProvider = services.BuildServiceProvider();
        var shellSettingsProvider = serviceProvider.GetRequiredService<CShells.Configuration.IShellSettingsProvider>();
        var shellSettingsCache = serviceProvider.GetRequiredService<CShells.Configuration.ShellSettingsCache>();
        shellSettingsCache.Load(shellSettingsProvider.GetShellSettingsAsync().GetAwaiter().GetResult().ToList());
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
