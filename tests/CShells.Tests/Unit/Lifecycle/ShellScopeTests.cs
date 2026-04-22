using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Lifecycle;

public class ShellScopeTests
{
    private static readonly ShellDescriptor Descriptor = ShellDescriptor.Create("test", 1);

    [Fact(DisplayName = "BeginScope increments the active-scope counter")]
    public void BeginScope_IncrementsCounter()
    {
        var shell = CreateShell();
        Assert.Equal(0, shell.ActiveScopeCount);

        var scope = shell.BeginScope();

        Assert.Equal(1, shell.ActiveScopeCount);
        Assert.Same(shell, scope.Shell);
        Assert.NotNull(scope.ServiceProvider);
    }

    [Fact(DisplayName = "Disposing a scope decrements the counter")]
    public async Task DisposeScope_DecrementsCounter()
    {
        var shell = CreateShell();
        var scope = shell.BeginScope();

        await scope.DisposeAsync();

        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Fact(DisplayName = "Double-dispose decrements the counter exactly once")]
    public async Task DoubleDispose_DecrementsOnce()
    {
        var shell = CreateShell();
        var scope = shell.BeginScope();

        await scope.DisposeAsync();
        await scope.DisposeAsync();

        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Fact(DisplayName = "BeginScope on a Disposed shell throws InvalidOperationException")]
    public async Task BeginScope_AfterDisposed_Throws()
    {
        var shell = CreateShell();
        await shell.DisposeAsync();

        Assert.Throws<InvalidOperationException>(() => shell.BeginScope());
    }

    [Fact(DisplayName = "BeginScope during Draining succeeds and counts toward the scope-wait phase")]
    public async Task BeginScope_DuringDraining_IsPermitted()
    {
        var shell = CreateShell();
        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        await shell.TryTransitionAsync(ShellLifecycleState.Active, ShellLifecycleState.Deactivating);
        await shell.TryTransitionAsync(ShellLifecycleState.Deactivating, ShellLifecycleState.Draining);

        var scope = shell.BeginScope();

        Assert.Equal(1, shell.ActiveScopeCount);
        Assert.Equal(ShellLifecycleState.Draining, shell.State);
        await scope.DisposeAsync();
    }

    [Fact(DisplayName = "Concurrent BeginScope + Dispose leaves counter at zero")]
    public async Task ConcurrentBeginAndDispose_ResultsInZero()
    {
        var shell = CreateShell();

        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            var s = shell.BeginScope();
            await Task.Yield();
            await s.DisposeAsync();
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(0, shell.ActiveScopeCount);
    }

    [Fact(DisplayName = "WaitForScopesReleasedAsync completes immediately when no scopes are active")]
    public async Task WaitForScopesReleased_ImmediateWhenIdle()
    {
        var shell = CreateShell();

        await shell.WaitForScopesReleasedAsync();
    }

    [Fact(DisplayName = "WaitForScopesReleasedAsync completes when the last scope is disposed")]
    public async Task WaitForScopesReleased_CompletesAfterLastDispose()
    {
        var shell = CreateShell();
        var s1 = shell.BeginScope();
        var s2 = shell.BeginScope();

        var wait = shell.WaitForScopesReleasedAsync();
        Assert.False(wait.IsCompleted);

        await s1.DisposeAsync();
        Assert.False(wait.IsCompleted);

        await s2.DisposeAsync();
        await wait.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static Shell CreateShell()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new Shell(Descriptor, sp, (_, _, _) => Task.CompletedTask);
    }
}
