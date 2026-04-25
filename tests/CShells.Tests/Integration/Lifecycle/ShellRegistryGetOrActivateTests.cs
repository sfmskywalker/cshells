using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using CShells.Lifecycle.Providers;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryGetOrActivateTests
{
    [Fact(DisplayName = "GetOrActivateAsync with a never-activated name calls provider once and activates generation 1")]
    public async Task GetOrActivate_CallsProviderOnce_Activates()
    {
        var stub = new StubShellBlueprintProvider()
            .Add("acme-42");
        await using var host = BuildHostWith(stub);
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.GetOrActivateAsync("acme-42");

        Assert.Equal("acme-42", shell.Descriptor.Name);
        Assert.Equal(1, shell.Descriptor.Generation);
        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(1, stub.LookupCount);
    }

    [Fact(DisplayName = "GetOrActivateAsync with an already-active shell returns it without re-querying the provider")]
    public async Task GetOrActivate_AlreadyActive_NoRequery()
    {
        var stub = new StubShellBlueprintProvider().Add("acme-42");
        await using var host = BuildHostWith(stub);
        var registry = host.GetRequiredService<IShellRegistry>();

        var first = await registry.GetOrActivateAsync("acme-42");
        var countAfterFirst = stub.LookupCount;

        var second = await registry.GetOrActivateAsync("acme-42");

        Assert.Same(first, second);
        Assert.Equal(countAfterFirst, stub.LookupCount);
    }

    [Fact(DisplayName = "GetOrActivateAsync: stampede of 100 concurrent callers triggers exactly one provider lookup")]
    public async Task GetOrActivate_Stampede_SingleLookup()
    {
        var stub = new StubShellBlueprintProvider().Add("hot");
        await using var host = BuildHostWith(stub);
        var registry = host.GetRequiredService<IShellRegistry>();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => registry.GetOrActivateAsync("hot"))
            .ToArray();

        var shells = await Task.WhenAll(tasks);

        // All callers see the same instance.
        Assert.All(shells, s => Assert.Same(shells[0], s));
        // Provider was consulted exactly once.
        Assert.Equal(1, stub.LookupCount);
    }

    [Fact(DisplayName = "GetOrActivateAsync: provider returns null → ShellBlueprintNotFoundException, no partial state")]
    public async Task GetOrActivate_ProviderMiss_NotFound()
    {
        var stub = new StubShellBlueprintProvider();  // empty
        await using var host = BuildHostWith(stub);
        var registry = host.GetRequiredService<IShellRegistry>();

        var ex = await Assert.ThrowsAsync<ShellBlueprintNotFoundException>(
            () => registry.GetOrActivateAsync("missing"));

        Assert.Equal("missing", ex.Name);
        Assert.Null(registry.GetActive("missing"));
    }

    [Fact(DisplayName = "GetOrActivateAsync: provider throws → ShellBlueprintUnavailableException wrapping inner cause; state stays null; subsequent call retries")]
    public async Task GetOrActivate_ProviderThrows_Unavailable_Retryable()
    {
        var stub = new StubShellBlueprintProvider { ThrowOnGet = new ApplicationException("boom") };
        await using var host = BuildHostWith(stub);
        var registry = host.GetRequiredService<IShellRegistry>();

        var ex1 = await Assert.ThrowsAsync<ShellBlueprintUnavailableException>(
            () => registry.GetOrActivateAsync("flaky"));
        Assert.IsType<ApplicationException>(ex1.InnerException);
        Assert.Null(registry.GetActive("flaky"));

        // Heal and retry.
        stub.ThrowOnGet = null;
        stub.Add("flaky");

        var shell = await registry.GetOrActivateAsync("flaky");
        Assert.Equal(1, shell.Descriptor.Generation);
    }

    [Fact(DisplayName = "Startup does not enumerate the catalogue (SC-001)")]
    public async Task Startup_DoesNotEnumerateCatalogue()
    {
        var stub = new StubShellBlueprintProvider();
        for (var i = 0; i < 100_000; i++)
            stub.Add($"tenant-{i:D6}");

        await using var host = BuildHostWith(stub);

        // If startup enumerated, ListCount would be > 0.
        Assert.Equal(0, stub.ListCount);
        // And no blueprint should be consulted either.
        Assert.Equal(0, stub.LookupCount);
    }

    private static ServiceProvider BuildHostWith(StubShellBlueprintProvider stub)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCShells(cshells =>
        {
            cshells.WithAssemblies();  // no feature discovery
            cshells.AddBlueprintProvider(_ => stub);
        });
        return services.BuildServiceProvider();
    }
}
