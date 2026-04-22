using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Lifecycle.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryDrainTests
{
    [Fact(DisplayName = "DrainAsync with zero handlers completes immediately (SC-009)")]
    public async Task Drain_WithNoHandlers_CompletesImmediately()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryDrainTests>()
            .AddShell("plain", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("plain");

        var op = await registry.DrainAsync(shell);
        var result = await op.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DrainStatus.Completed, result.Status);
        Assert.Empty(result.HandlerResults);
        Assert.Equal(0, result.AbandonedScopeCount);
        Assert.Equal(ShellLifecycleState.Disposed, shell.State);
    }

    [Theory(DisplayName = "DrainAsync invokes 0/1/50 handlers in parallel (SC-004)")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    public async Task Drain_InvokesHandlersInParallel(int handlerCount)
    {
        RecordingDrainFeature.HandlerCount = handlerCount;
        var hits = RecordingDrainFeature.Hits = [];

        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryDrainTests>()
            .AddShell("workflow", s => s.WithFeature<RecordingDrainFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("workflow");

        var op = await registry.DrainAsync(shell);
        var result = await op.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DrainStatus.Completed, result.Status);
        Assert.Equal(handlerCount, result.HandlerResults.Count);
        Assert.Equal(handlerCount, hits.Count);
        Assert.All(result.HandlerResults, r => Assert.True(r.Completed));
        Assert.All(result.HandlerResults, r => Assert.Null(r.Error));
    }

    [Fact(DisplayName = "Throwing handler is captured in DrainHandlerResult.Error without aborting peers")]
    public async Task Drain_ThrowingHandler_DoesNotAbortPeers()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryDrainTests>()
            .AddShell("mixed", s => s.WithFeature<MixedDrainFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("mixed");

        var op = await registry.DrainAsync(shell);
        var result = await op.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, result.HandlerResults.Count);
        var good = result.HandlerResults.Single(r => r.HandlerTypeName == nameof(GoodDrainHandler));
        var bad = result.HandlerResults.Single(r => r.HandlerTypeName == nameof(ThrowingDrainHandler));
        Assert.True(good.Completed);
        Assert.Null(good.Error);
        Assert.False(bad.Completed);
        Assert.IsType<ApplicationException>(bad.Error);
    }

    [Fact(DisplayName = "Concurrent DrainAsync for the same shell returns the same operation (FR-028, SC-006)")]
    public async Task Drain_Concurrent_ReturnsSameOperation()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryDrainTests>()
            .AddShell("slow", s => s.WithFeature<SlowDrainFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("slow");

        var ops = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => registry.DrainAsync(shell)));

        Assert.All(ops, o => Assert.Same(ops[0], o));
        await ops[0].WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "Fixed-timeout policy cancels handler after deadline → TimedOut status")]
    public async Task Drain_FixedTimeout_TimesOut()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryDrainTests>()
            .AddShell("stuck", s => s.WithFeature<StuckDrainFeature>()));

        // Override drain policy to 150 ms for this test.
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddCShells(cshells =>
        {
            cshells.WithAssemblyContaining<ShellRegistryDrainTests>();
            cshells.AddShell("stuck", s => s.WithFeature<StuckDrainFeature>());
            cshells.Services.Replace(ServiceDescriptor.Singleton<IDrainPolicy>(_ => new FixedTimeoutDrainPolicy(TimeSpan.FromMilliseconds(150))));
            cshells.Services.Replace(ServiceDescriptor.Singleton(new DrainGracePeriod(TimeSpan.FromMilliseconds(200))));
        });

        await using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("stuck");

        var op = await registry.DrainAsync(shell);
        var result = await op.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DrainStatus.TimedOut, result.Status);
        Assert.False(result.HandlerResults[0].Completed);
        Assert.Equal(ShellLifecycleState.Disposed, shell.State);
    }

    // =================================================================
    // Test doubles
    // =================================================================

    public sealed class RecordingDrainFeature : IShellFeature
    {
        public static int HandlerCount;
        public static System.Collections.Concurrent.ConcurrentBag<int> Hits = [];

        public void ConfigureServices(IServiceCollection services)
        {
            for (var i = 0; i < HandlerCount; i++)
                services.AddTransient<IDrainHandler, RecordingDrainHandler>();
        }
    }

    private sealed class RecordingDrainHandler : IDrainHandler
    {
        public Task DrainAsync(IDrainExtensionHandle _, CancellationToken ct)
        {
            RecordingDrainFeature.Hits.Add(Environment.CurrentManagedThreadId);
            return Task.CompletedTask;
        }
    }

    public sealed class MixedDrainFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IDrainHandler, GoodDrainHandler>();
            services.AddTransient<IDrainHandler, ThrowingDrainHandler>();
        }
    }

    private sealed class GoodDrainHandler : IDrainHandler
    {
        public Task DrainAsync(IDrainExtensionHandle _, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ThrowingDrainHandler : IDrainHandler
    {
        public Task DrainAsync(IDrainExtensionHandle _, CancellationToken ct) => throw new ApplicationException("boom");
    }

    public sealed class SlowDrainFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddTransient<IDrainHandler, SlowDrainHandler>();
    }

    private sealed class SlowDrainHandler : IDrainHandler
    {
        public Task DrainAsync(IDrainExtensionHandle _, CancellationToken ct) => Task.Delay(200, ct);
    }

    public sealed class StuckDrainFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddTransient<IDrainHandler, StuckDrainHandler>();
    }

    private sealed class StuckDrainHandler : IDrainHandler
    {
        public async Task DrainAsync(IDrainExtensionHandle _, CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }
}
