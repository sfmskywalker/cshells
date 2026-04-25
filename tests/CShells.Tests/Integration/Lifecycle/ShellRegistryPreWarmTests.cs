using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryPreWarmTests
{
    [Fact(DisplayName = "PreWarmShells activates every listed name at startup; unrelated shells remain inactive")]
    public async Task PreWarm_ActivatesNamedShells()
    {
        var stub = new StubShellBlueprintProvider()
            .Add("hot-a")
            .Add("hot-b")
            .Add("cold");

        using var host = BuildHostWith(stub, preWarm: ["hot-a", "hot-b"]);
        await host.StartAsync();
        try
        {
            var registry = host.Services.GetRequiredService<IShellRegistry>();

            Assert.NotNull(registry.GetActive("hot-a"));
            Assert.NotNull(registry.GetActive("hot-b"));
            Assert.Null(registry.GetActive("cold"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact(DisplayName = "PreWarmShells continues on individual failures and activates the rest")]
    public async Task PreWarm_ContinuesOnFailure()
    {
        var stub = new StubShellBlueprintProvider()
            .Add("good");
        // "bad" is never added to the stub — provider.GetAsync will return null for it.

        using var host = BuildHostWith(stub, preWarm: ["good", "bad"]);
        // Should not throw even though "bad" can't be activated.
        await host.StartAsync();
        try
        {
            var registry = host.Services.GetRequiredService<IShellRegistry>();
            Assert.NotNull(registry.GetActive("good"));
            Assert.Null(registry.GetActive("bad"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static IHost BuildHostWith(StubShellBlueprintProvider stub, string[] preWarm)
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddCShells(cshells =>
                {
                    cshells.WithAssemblies();
                    cshells.AddBlueprintProvider(_ => stub);
                    cshells.PreWarmShells(preWarm);
                });
            })
            .Build();
    }
}
