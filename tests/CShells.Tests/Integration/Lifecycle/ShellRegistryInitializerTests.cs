using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.Lifecycle;

/// <summary>
/// Integration tests for US4: <see cref="IShellInitializer"/> invocation during
/// <see cref="IShellRegistry.ActivateAsync"/>.
/// </summary>
public class ShellRegistryInitializerTests
{
    [Fact(DisplayName = "Initializers run sequentially in DI-registration order before the shell becomes Active")]
    public async Task Initializers_RunSequentially_BeforeActive()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<TwoInitializersFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("payments");

        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(["first", "second"], collector.Invocations);
        Assert.All(collector.StatesSeenInsideInitializer, s => Assert.Equal(ShellLifecycleState.Initializing, s));
    }

    [Fact(DisplayName = "Initializer exception propagates and leaves no partial entry")]
    public async Task InitializerException_Propagates_NoPartialEntry()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<ThrowingInitializerFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await Assert.ThrowsAsync<ApplicationException>(() => registry.ActivateAsync("payments"));

        Assert.Null(registry.GetActive("payments"));
        Assert.Empty(registry.GetAll("payments"));
    }

    [Fact(DisplayName = "Shell with no initializers activates immediately")]
    public async Task NoInitializers_ActivatesImmediately()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("plain", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("plain");

        Assert.Equal(ShellLifecycleState.Active, shell.State);
    }

    // =================================================================
    // Test doubles
    // =================================================================

    private static ServiceProvider BuildHostWithCollector(InitOrderCollector collector, Action<CShellsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Register the collector in root services BEFORE AddCShells; shells inherit root
        // registrations, so the initializers resolve it cleanly via their constructor.
        services.AddSingleton(collector);
        services.AddCShells(configure);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildHost(Action<CShellsBuilder> configure) =>
        ShellRegistryActivateTests.BuildHost(configure);

    public sealed class InitOrderCollector
    {
        public List<string> Invocations { get; } = [];
        public List<ShellLifecycleState> StatesSeenInsideInitializer { get; } = [];
    }

    private sealed class FirstInitializer(IShell shell, InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("first");
            collector.StatesSeenInsideInitializer.Add(shell.State);
            return Task.CompletedTask;
        }
    }

    private sealed class SecondInitializer(IShell shell, InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("second");
            collector.StatesSeenInsideInitializer.Add(shell.State);
            return Task.CompletedTask;
        }
    }

    public sealed class TwoInitializersFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IShellInitializer, FirstInitializer>();
            services.AddTransient<IShellInitializer, SecondInitializer>();
        }
    }

    private sealed class ThrowingInitializer : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => throw new ApplicationException("init fail");
    }

    public sealed class ThrowingInitializerFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IShellInitializer, ThrowingInitializer>();
        }
    }
}
