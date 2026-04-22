using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Lifecycle.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CShells.Tests.Integration.Lifecycle;

public class ShellRegistryScopeWaitTests
{
    [Fact(DisplayName = "Outstanding scopes delay drain handler invocation (FR-022)")]
    public async Task OutstandingScopes_DelayHandlers()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GateDrainFeature.HandlerStarted = handlerStarted;

        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryScopeWaitTests>()
            .AddShell("gated", s => s.WithFeature<GateDrainFeature>()));

        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("gated");

        // Acquire a scope — drain's phase 1 must wait for it.
        var scope = shell.BeginScope();

        var drain = await registry.DrainAsync(shell);

        // Handler must NOT have started while the scope is outstanding.
        var quickTimeout = Task.Delay(150);
        var earlyStart = await Task.WhenAny(handlerStarted.Task, quickTimeout);
        Assert.Same(quickTimeout, earlyStart);

        // Release the scope; handler now starts, drain completes.
        await scope.DisposeAsync();

        var result = await drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DrainStatus.Completed, result.Status);
        Assert.Equal(0, result.AbandonedScopeCount);
    }

    [Fact(DisplayName = "Scopes outstanding at deadline are abandoned (SC-010 — counter reflects it)")]
    public async Task ScopesOutstandingAtDeadline_AreAbandoned()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddCShells(cshells =>
        {
            cshells.WithAssemblyContaining<ShellRegistryScopeWaitTests>();
            cshells.AddShell("short", _ => { });
            cshells.Services.Replace(ServiceDescriptor.Singleton<IDrainPolicy>(_ =>
                new FixedTimeoutDrainPolicy(TimeSpan.FromMilliseconds(100))));
            cshells.Services.Replace(ServiceDescriptor.Singleton(new DrainGracePeriod(TimeSpan.FromMilliseconds(200))));
        });

        await using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("short");

        var scope = shell.BeginScope();

        var drain = await registry.DrainAsync(shell);
        var result = await drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // With no handlers, status is Completed even when scope-wait elapses (no handler
        // was running to report a timeout). The AbandonedScopeCount surfaces the fact.
        Assert.Equal(1, result.AbandonedScopeCount);
        Assert.True(result.ScopeWaitElapsed >= TimeSpan.FromMilliseconds(50));
        Assert.Equal(ShellLifecycleState.Disposed, shell.State);

        // Releasing the (now orphaned) scope handle is still safe.
        await scope.DisposeAsync();
    }

    [Fact(DisplayName = "Scope-wait completes immediately when no scopes are active")]
    public async Task ScopeWait_CompletesImmediately_WhenIdle()
    {
        await using var host = ShellRegistryActivateTests.BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryScopeWaitTests>()
            .AddShell("idle", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();
        var shell = await registry.ActivateAsync("idle");

        var drain = await registry.DrainAsync(shell);
        var result = await drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, result.AbandonedScopeCount);
        Assert.True(result.ScopeWaitElapsed < TimeSpan.FromMilliseconds(200));
    }

    public sealed class GateDrainFeature : IShellFeature
    {
        public static TaskCompletionSource? HandlerStarted;

        public void ConfigureServices(IServiceCollection services) =>
            services.AddTransient<IDrainHandler, GateHandler>();
    }

    private sealed class GateHandler : IDrainHandler
    {
        public Task DrainAsync(IDrainExtensionHandle _, CancellationToken ct)
        {
            GateDrainFeature.HandlerStarted?.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
