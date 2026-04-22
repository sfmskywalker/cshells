using CShells.DependencyInjection;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryReloadAllTests
{
    [Fact(DisplayName = "ReloadAllAsync reloads every registered blueprint")]
    public async Task ReloadAll_ReloadsEveryBlueprint()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadAllTests>()
            .AddShell("a", _ => { })
            .AddShell("b", _ => { })
            .AddShell("c", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        await registry.ActivateAsync("a");
        await registry.ActivateAsync("b");
        await registry.ActivateAsync("c");

        var results = await registry.ReloadAllAsync();

        Assert.Equal(3, results.Count);
        Assert.All(results, r =>
        {
            Assert.Null(r.Error);
            Assert.NotNull(r.NewShell);
            Assert.Equal(2, r.NewShell!.Descriptor.Generation);
        });
    }

    [Fact(DisplayName = "ReloadAllAsync: one failing blueprint surfaces error; others still reload (FR-012)")]
    public async Task ReloadAll_PerNameFailure_DoesNotAbortBatch()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadAllTests>()
            .AddShell("a", _ => { })
            .AddShell("c", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        registry.RegisterBlueprint(new FailingBlueprint("b"));
        await registry.ActivateAsync("a");
        await registry.ActivateAsync("c");

        var results = await registry.ReloadAllAsync();

        var a = results.Single(r => r.Name == "a");
        var b = results.Single(r => r.Name == "b");
        var c = results.Single(r => r.Name == "c");

        Assert.Null(a.Error);
        Assert.NotNull(a.NewShell);

        Assert.NotNull(b.Error);
        Assert.Null(b.NewShell);

        Assert.Null(c.Error);
        Assert.NotNull(c.NewShell);
    }

    [Fact(DisplayName = "ReloadAllAsync activates never-activated shells as generation 1")]
    public async Task ReloadAll_ActivatesNeverActivatedShells()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryReloadAllTests>()
            .AddShell("fresh", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();

        var results = await registry.ReloadAllAsync();

        var r = Assert.Single(results);
        Assert.Null(r.Error);
        Assert.Null(r.Drain); // No prior generation.
        Assert.Equal(1, r.NewShell!.Descriptor.Generation);
    }

    private sealed class FailingBlueprint(string name) : IShellBlueprint
    {
        public string Name { get; } = name;
        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();
        public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
            => throw new ApplicationException("boom");
    }
}
