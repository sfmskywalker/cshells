using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryConfigureAllShellsTests
{
    [Fact(DisplayName = "ConfigureAllShells features appear on code-seeded (AddShell) shells")]
    public async Task ConfigureAllShells_AppliedTo_CodeSeededShell()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblies()
            .ConfigureAllShells(shell => shell.WithFeatures("CommonFeature"))
            .AddShell("payments", shell => shell.WithFeatures("PaymentsFeature")));

        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("payments");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(["CommonFeature", "PaymentsFeature"], settings.EnabledFeatures);
    }

    [Fact(DisplayName = "ConfigureAllShells features appear on configuration-based shells")]
    public async Task ConfigureAllShells_AppliedTo_ConfigBasedShell()
    {
        // Build a minimal IConfiguration that defines a shell with one feature.
        var configData = new Dictionary<string, string?>
        {
            ["CShells:Shells:0:Name"] = "catalog",
            ["CShells:Shells:0:Features:0"] = "ShellSpecificFeature",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        await using var host = BuildHostWithConfiguration(configuration, cshells => cshells
            .ConfigureAllShells(shell => shell.WithFeatures("CommonFeature")));

        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("catalog");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(["CommonFeature", "ShellSpecificFeature"], settings.EnabledFeatures);
    }

    [Fact(DisplayName = "ConfigureAllShells adds features without duplicating existing ones")]
    public async Task ConfigureAllShells_DeduplicatesFeatures()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblies()
            .ConfigureAllShells(shell => shell.WithFeatures("Shared"))
            .AddShell("payments", shell => shell.WithFeatures("Shared", "Extra")));

        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("payments");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(["Shared", "Extra"], settings.EnabledFeatures);
    }

    [Fact(DisplayName = "Shell-specific configuration overrides ConfigureAllShells defaults")]
    public async Task ConfigureAllShells_ShellSpecificConfigurationWins()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblies()
            .ConfigureAllShells(shell => shell
                .WithConfiguration("Plan", "Standard")
                .WithConfiguration("Region", "Global"))
            .AddShell("payments", shell => shell.WithConfiguration("Plan", "Enterprise")));

        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("payments");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Equal("Enterprise", settings.ConfigurationData["Plan"]);
        Assert.Equal("Global", settings.ConfigurationData["Region"]);
    }

    [Fact(DisplayName = "Configuration-based shell configuration overrides ConfigureAllShells defaults")]
    public async Task ConfigureAllShells_ConfigBasedConfigurationWins()
    {
        var configData = new Dictionary<string, string?>
        {
            ["CShells:Shells:0:Name"] = "catalog",
            ["CShells:Shells:0:Configuration:Plan"] = "Enterprise",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        await using var host = BuildHostWithConfiguration(configuration, cshells => cshells
            .ConfigureAllShells(shell => shell
                .WithConfiguration("Plan", "Standard")
                .WithConfiguration("Region", "Global")));

        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("catalog");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.Equal("Enterprise", settings.ConfigurationData["Plan"]);
        Assert.Equal("Global", settings.ConfigurationData["Region"]);
    }

    private static ServiceProvider BuildHostWithConfiguration(
        IConfiguration configuration,
        Action<CShellsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCShells(cshells =>
        {
            cshells.WithAssemblies();
            cshells.WithConfigurationProvider(configuration);
            configure(cshells);
        });
        return services.BuildServiceProvider();
    }
}
