using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using CShells.Lifecycle.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryActivateTests
{
    [Fact(DisplayName = "ActivateAsync stamps generation 1 and promotes to Active")]
    public async Task ActivateAsync_StampsGeneration1_AndPromotesToActive()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblies() // explicit empty — no feature discovery
            .AddShell("payments", _ => { }));

        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("payments");

        Assert.Equal("payments", shell.Descriptor.Name);
        Assert.Equal(1, shell.Descriptor.Generation);
        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Same(shell, registry.GetActive("payments"));
        Assert.Equal([shell], registry.GetAll("payments"));
    }

    [Fact(DisplayName = "ActivateAsync on a name with no blueprint throws ShellBlueprintNotFoundException")]
    public async Task ActivateAsync_WithoutBlueprint_Throws()
    {
        await using var host = BuildHost(cshells => cshells.WithAssemblies());
        var registry = host.GetRequiredService<IShellRegistry>();

        var ex = await Assert.ThrowsAsync<ShellBlueprintNotFoundException>(() => registry.ActivateAsync("unknown"));
        Assert.Equal("unknown", ex.Name);
    }

    [Fact(DisplayName = "ActivateAsync twice on the same name throws (caller should use ReloadAsync)")]
    public async Task ActivateAsync_WhenAlreadyActive_Throws()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblies()
            .AddShell("payments", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("payments");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ActivateAsync("payments"));
        Assert.Contains("Active", ex.Message);
        Assert.Contains("ReloadAsync", ex.Message);
    }

    [Fact(DisplayName = "Duplicate blueprint registration in the in-memory provider throws")]
    public void DuplicateBlueprint_Throws()
    {
        // The in-memory provider is self-contained; no host needed to assert its duplicate guard.
        var provider = new InMemoryShellBlueprintProvider();
        provider.Add(new DelegateShellBlueprint("payments", _ => { }));

        Assert.Throws<InvalidOperationException>(() =>
            provider.Add(new DelegateShellBlueprint("Payments", _ => { })));
    }

    [Fact(DisplayName = "Blueprint composition exception propagates and leaves no partial entry")]
    public async Task CompositionException_Propagates_NoPartialEntry()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblies()
            .AddBlueprint(new ThrowingBlueprint("payments")));
        var registry = host.GetRequiredService<IShellRegistry>();

        await Assert.ThrowsAsync<ApplicationException>(() => registry.ActivateAsync("payments"));

        Assert.Null(registry.GetActive("payments"));
        Assert.Empty(registry.GetAll("payments"));
    }

    [Fact(DisplayName = "Blueprint name mismatch in composed settings throws")]
    public async Task Blueprint_NameMismatch_Throws()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblies()
            .AddBlueprint(new NameMismatchBlueprint("payments")));
        var registry = host.GetRequiredService<IShellRegistry>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ActivateAsync("payments"));
        Assert.Contains("blueprint name mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "IShell is resolvable from the shell's own provider")]
    public async Task IShell_IsResolvable_FromShellProvider()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblies()
            .AddShell("payments", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("payments");
        var resolved = shell.ServiceProvider.GetRequiredService<IShell>();

        Assert.Same(shell, resolved);
    }

    internal static ServiceProvider BuildHost(Action<CShellsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCShells(configure);
        return services.BuildServiceProvider();
    }

    private sealed class ThrowingBlueprint(string name) : IShellBlueprint
    {
        public string Name { get; } = name;
        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

        public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
            => throw new ApplicationException("compose fail");
    }

    private sealed class NameMismatchBlueprint(string name) : IShellBlueprint
    {
        public string Name { get; } = name;
        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

        public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ShellSettings(new ShellId("other-name")));
    }
}
