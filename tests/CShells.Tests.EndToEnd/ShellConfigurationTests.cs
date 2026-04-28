using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.EndToEnd;

/// <summary>
/// Tests to verify shell configuration is loaded correctly from JSON files.
/// </summary>
[Collection("Workbench")]
public class ShellConfigurationTests(WorkbenchApplicationFactory factory)
{
    [Fact(DisplayName = "All three shells activate as generation 1")]
    public async Task AllShells_AreActivated()
    {
        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IShellRegistry>();

        var page = await registry.ListAsync(new ShellListQuery(Limit: 50));
        var names = page.Items.Select(s => s.Name).ToList();
        Assert.Contains("Default", names);
        Assert.Contains("Acme", names);
        Assert.Contains("Contoso", names);

        foreach (var name in new[] { "Default", "Acme", "Contoso" })
        {
            var shell = await registry.GetOrActivateAsync(name);
            Assert.Equal(ShellLifecycleState.Active, shell.State);
            Assert.Equal(1, shell.Descriptor.Generation);
        }
    }

    [Fact(DisplayName = "Shell configuration contains WebRouting path mappings")]
    public async Task ShellConfiguration_ContainsPathMappings()
    {
        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IShellRegistry>();

        var defaultShell = await registry.GetOrActivateAsync("Default");
        var acmeShell = await registry.GetOrActivateAsync("Acme");
        var contosoShell = await registry.GetOrActivateAsync("Contoso");

        var defaultSettings = defaultShell.ServiceProvider.GetRequiredService<ShellSettings>();
        var acmeSettings = acmeShell.ServiceProvider.GetRequiredService<ShellSettings>();
        var contosoSettings = contosoShell.ServiceProvider.GetRequiredService<ShellSettings>();

        Assert.True(defaultSettings.ConfigurationData.ContainsKey("WebRouting:Path"));
        Assert.True(acmeSettings.ConfigurationData.ContainsKey("WebRouting:Path"));
        Assert.True(contosoSettings.ConfigurationData.ContainsKey("WebRouting:Path"));

        var defaultPath = defaultSettings.GetConfiguration("WebRouting:Path");
        var acmePath = acmeSettings.GetConfiguration("WebRouting:Path");
        var contosoPath = contosoSettings.GetConfiguration("WebRouting:Path");

        Assert.True(string.IsNullOrEmpty(defaultPath));
        Assert.Equal("acme", acmePath);
        Assert.Equal("contoso", contosoPath);
    }
}
