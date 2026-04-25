using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using CShells.Lifecycle.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryReloadTests
{
    [Fact(DisplayName = "ReloadAsync on an inactive name activates generation 1 (FR-011)")]
    public async Task Reload_FirstTime_BehavesLikeActivate()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadTests>()
            .AddShell("payments", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();

        var result = await registry.ReloadAsync("payments");

        Assert.Null(result.Error);
        Assert.NotNull(result.NewShell);
        Assert.Null(result.Drain); // no prior generation
        Assert.Equal(1, result.NewShell!.Descriptor.Generation);
        Assert.Equal(ShellLifecycleState.Active, result.NewShell.State);
    }

    [Fact(DisplayName = "ReloadAsync promotes gen+1 and drains previous generation")]
    public async Task Reload_PromotesNext_AndDrainsPrevious()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadTests>()
            .AddShell("payments", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();

        var gen1 = await registry.ActivateAsync("payments");
        var result = await registry.ReloadAsync("payments");

        Assert.Null(result.Error);
        Assert.NotNull(result.NewShell);
        Assert.NotNull(result.Drain);
        Assert.Equal(2, result.NewShell!.Descriptor.Generation);
        Assert.Same(result.NewShell, registry.GetActive("payments"));

        await result.Drain!.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ShellLifecycleState.Disposed, gen1.State);
        Assert.Equal(ShellLifecycleState.Active, result.NewShell.State);
    }

    [Fact(DisplayName = "ReloadAsync with no blueprint throws ShellBlueprintNotFoundException")]
    public async Task Reload_NoBlueprint_Throws()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadTests>());
        var registry = host.GetRequiredService<IShellRegistry>();

        var ex = await Assert.ThrowsAsync<ShellBlueprintNotFoundException>(() => registry.ReloadAsync("unknown"));
        Assert.Equal("unknown", ex.Name);
    }

    [Fact(DisplayName = "Reload composition failure returns ReloadResult.Error and leaves active unchanged (FR-014)")]
    public async Task Reload_CompositionFailure_LeavesActiveUnchanged()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadTests>()
            .AddShell("flaky", _ => { })
            .AddBlueprint(new FailingOnReloadBlueprint("unstable")));
        var registry = host.GetRequiredService<IShellRegistry>();

        var gen1 = await registry.ActivateAsync("flaky");

        // The throwing blueprint was registered upfront — calling Reload triggers its
        // ComposeAsync, which throws and is captured into ReloadResult.Error.
        var result = await registry.ReloadAsync("unstable");

        Assert.NotNull(result.Error);
        Assert.Null(result.NewShell);
        Assert.Null(result.Drain);
        Assert.Null(registry.GetActive("unstable"));

        // Unrelated active is unaffected.
        Assert.Equal(ShellLifecycleState.Active, gen1.State);
        Assert.Same(gen1, registry.GetActive("flaky"));
    }

    [Fact(DisplayName = "Concurrent ReloadAsync for the same name serializes and assigns monotonic generations (FR-013)")]
    public async Task ConcurrentReloads_SerializeMonotonically()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadTests>()
            .AddShell("payments", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.ActivateAsync("payments");

        var reloads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => registry.ReloadAsync("payments")));

        var generations = reloads
            .Where(r => r.NewShell is not null)
            .Select(r => r.NewShell!.Descriptor.Generation)
            .OrderBy(g => g)
            .ToList();

        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9], generations);
        Assert.Equal(9, registry.GetActive("payments")!.Descriptor.Generation);
    }

    [Fact(DisplayName = "Reload of different names runs in parallel")]
    public async Task ReloadDifferentNames_RunsInParallel()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadTests>()
            .AddShell("a", _ => { })
            .AddShell("b", _ => { })
            .AddShell("c", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.ActivateAsync("a");
        await registry.ActivateAsync("b");
        await registry.ActivateAsync("c");

        var results = await Task.WhenAll(
            registry.ReloadAsync("a"),
            registry.ReloadAsync("b"),
            registry.ReloadAsync("c"));

        Assert.All(results, r =>
        {
            Assert.Null(r.Error);
            Assert.NotNull(r.NewShell);
            Assert.Equal(2, r.NewShell!.Descriptor.Generation);
        });
    }

    private sealed class FailingOnReloadBlueprint(string name) : IShellBlueprint
    {
        public string Name { get; } = name;
        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

        public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
            => throw new ApplicationException("compose fail");
    }
}
