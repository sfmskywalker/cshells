using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Tests.Integration.ShellHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.DefaultShellHost;

public class FeatureConfigurationBindingIntegrationTests : IAsyncDisposable
{
    private readonly List<Hosting.DefaultShellHost> hostsToDispose = [];
    private readonly List<ServiceProvider> providersToDispose = [];

    [Fact(DisplayName = "GetShell binds ICollection feature property from feature configuration object")]
    public void GetShell_BindsICollectionProperty_FromFeatureConfigurationObject()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:0:Name"] = "Default",
                ["CShells:Shells:0:Features:0:Name"] = "DefaultAdminUser",
                ["CShells:Shells:0:Features:0:AdminRolePermissions:0"] = "ReadUsers",
                ["CShells:Shells:0:Features:0:AdminRolePermissions:1"] = "ManageUsers"
            })
            .Build();

        var shellSettings = ShellSettingsFactory.CreateFromConfiguration(configuration.GetSection("CShells:Shells:0"));
        var host = CreateHost(configuration, [shellSettings], [typeof(DefaultAdminUserCollectionBindingFeature).Assembly]);

        // Act
        var shell = host.GetShell(new("Default"));
        var snapshot = shell.ServiceProvider.GetRequiredService<AdminRolePermissionsSnapshot>();

        // Assert
        Assert.Equal(["ReadUsers", "ManageUsers"], snapshot.Permissions);
    }

    private Hosting.DefaultShellHost CreateHost(
        IConfiguration configuration,
        IReadOnlyList<ShellSettings> shellSettings,
        IReadOnlyList<Assembly> assemblies)
    {
        var cache = new ShellSettingsCache();
        cache.Load(shellSettings);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        var provider = services.BuildServiceProvider();
        providersToDispose.Add(provider);

        var accessor = TestFixtures.CreateRootServicesAccessor(services);
        var factory = new DefaultShellFeatureFactory(provider);
        var exclusionRegistry = new Hosting.ShellServiceExclusionRegistry([]);

        var host = new Hosting.DefaultShellHost(cache, assemblies, provider, accessor, factory, exclusionRegistry);
        hostsToDispose.Add(host);

        return host;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in hostsToDispose)
        {
            await host.DisposeAsync();
        }

        foreach (var provider in providersToDispose)
        {
            await provider.DisposeAsync();
        }
    }
}

[ShellFeature("DefaultAdminUser")]
public sealed class DefaultAdminUserCollectionBindingFeature : IShellFeature
{
    public ICollection<string> AdminRolePermissions { get; set; } = [];

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new AdminRolePermissionsSnapshot([.. AdminRolePermissions]));
    }
}

public sealed record AdminRolePermissionsSnapshot(IReadOnlyList<string> Permissions);


