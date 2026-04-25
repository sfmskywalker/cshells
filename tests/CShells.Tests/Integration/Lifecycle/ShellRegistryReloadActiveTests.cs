using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryReloadActiveTests
{
    [Fact(DisplayName = "ReloadActiveAsync reloads every currently-active shell")]
    public async Task ReloadActive_ReloadsEveryActiveShell()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadActiveTests>()
            .AddShell("a", _ => { })
            .AddShell("b", _ => { })
            .AddShell("c", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.ActivateAsync("a");
        await registry.ActivateAsync("b");
        await registry.ActivateAsync("c");

        var results = await registry.ReloadActiveAsync();

        Assert.Equal(3, results.Count);
        Assert.All(results, r =>
        {
            Assert.Null(r.Error);
            Assert.NotNull(r.NewShell);
            Assert.Equal(2, r.NewShell!.Descriptor.Generation);
        });
    }

    [Fact(DisplayName = "ReloadActiveAsync does NOT activate never-activated shells (007 semantics)")]
    public async Task ReloadActive_LeavesInactiveShellsInactive()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadActiveTests>()
            .AddShell("a", _ => { })
            .AddShell("inactive", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.ActivateAsync("a");

        var results = await registry.ReloadActiveAsync();

        Assert.Single(results);
        Assert.Equal("a", results[0].Name);
        Assert.Null(registry.GetActive("inactive"));
    }

    [Fact(DisplayName = "ReloadActiveAsync respects MaxDegreeOfParallelism")]
    public async Task ReloadActive_RespectsMaxDegreeOfParallelism()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadActiveTests>()
            .AddShell("a", _ => { })
            .AddShell("b", _ => { })
            .AddShell("c", _ => { })
            .AddShell("d", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.ActivateAsync("a");
        await registry.ActivateAsync("b");
        await registry.ActivateAsync("c");
        await registry.ActivateAsync("d");

        var results = await registry.ReloadActiveAsync(new ReloadOptions(MaxDegreeOfParallelism: 2));

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.Null(r.Error));
    }
}
