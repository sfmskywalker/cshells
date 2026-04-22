using System.Collections.Concurrent;
using System.Reflection;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Lifecycle;

/// <summary>
/// Tests for the CAS-based <see cref="Shell"/> state machine and its state-change fan-out.
/// Guards FR-017 / FR-018 / FR-019 / FR-037.
/// </summary>
public class ShellStateMachineTests
{
    private static readonly ShellDescriptor Descriptor = ShellDescriptor.Create("test", 1);

    [Fact(DisplayName = "New shell starts in Initializing")]
    public void NewShell_StartsInInitializing()
    {
        var (shell, _) = CreateShell();

        Assert.Equal(ShellLifecycleState.Initializing, shell.State);
        Assert.Equal("test", shell.Descriptor.Name);
        Assert.Equal(1, shell.Descriptor.Generation);
    }

    [Fact(DisplayName = "Forward transitions succeed and fire events in order")]
    public async Task ForwardTransitions_FireEventsInOrder()
    {
        var (shell, events) = CreateShell();

        Assert.True(await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active));
        Assert.True(await shell.TryTransitionAsync(ShellLifecycleState.Active, ShellLifecycleState.Deactivating));
        Assert.True(await shell.TryTransitionAsync(ShellLifecycleState.Deactivating, ShellLifecycleState.Draining));
        Assert.True(await shell.TryTransitionAsync(ShellLifecycleState.Draining, ShellLifecycleState.Drained));

        Assert.Equal(ShellLifecycleState.Drained, shell.State);
        Assert.Equal(
            [
                (ShellLifecycleState.Initializing, ShellLifecycleState.Active),
                (ShellLifecycleState.Active, ShellLifecycleState.Deactivating),
                (ShellLifecycleState.Deactivating, ShellLifecycleState.Draining),
                (ShellLifecycleState.Draining, ShellLifecycleState.Drained),
            ],
            events);
    }

    [Fact(DisplayName = "TryTransition with wrong expected state returns false and fires no event")]
    public async Task TryTransition_WithWrongExpected_ReturnsFalse()
    {
        var (shell, events) = CreateShell();
        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        events.Clear();

        var ok = await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);

        Assert.False(ok);
        Assert.Empty(events);
    }

    [Fact(DisplayName = "TryTransition with backward target throws ArgumentOutOfRangeException")]
    public async Task TryTransition_BackwardThrows()
    {
        var (shell, _) = CreateShell();
        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await shell.TryTransitionAsync(ShellLifecycleState.Active, ShellLifecycleState.Initializing));
    }

    [Fact(DisplayName = "Concurrent transitions: exactly one caller wins")]
    public async Task Concurrent_TryTransition_ExactlyOneWins()
    {
        var (shell, events) = CreateShell();

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(async () => await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active))));

        Assert.Single(results, r => r);
        Assert.Equal(15, results.Count(r => !r));
        Assert.Single(events);
    }

    [Fact(DisplayName = "ForceAdvance skips intermediate states and fires only one event for the jump")]
    public async Task ForceAdvance_SkipsIntermediates()
    {
        var (shell, events) = CreateShell();

        await shell.ForceAdvanceAsync(ShellLifecycleState.Drained);

        Assert.Equal(ShellLifecycleState.Drained, shell.State);
        Assert.Equal([(ShellLifecycleState.Initializing, ShellLifecycleState.Drained)], events);
    }

    [Fact(DisplayName = "ForceAdvance to an earlier state is a no-op")]
    public async Task ForceAdvance_Backwards_IsNoOp()
    {
        var (shell, events) = CreateShell();
        await shell.ForceAdvanceAsync(ShellLifecycleState.Drained);
        events.Clear();

        await shell.ForceAdvanceAsync(ShellLifecycleState.Active);

        Assert.Equal(ShellLifecycleState.Drained, shell.State);
        Assert.Empty(events);
    }

    [Fact(DisplayName = "DisposeAsync transitions any non-terminal state directly to Disposed")]
    public async Task DisposeAsync_FromAnyState_GoesToDisposed()
    {
        var (shell, events) = CreateShell();
        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        events.Clear();

        await shell.DisposeAsync();

        Assert.Equal(ShellLifecycleState.Disposed, shell.State);
        Assert.Single(events);
        Assert.Equal((ShellLifecycleState.Active, ShellLifecycleState.Disposed), events.First());
    }

    [Fact(DisplayName = "DisposeAsync disposes the backing service provider")]
    public async Task DisposeAsync_DisposesProvider()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var shell = new Shell(Descriptor, sp, (_, _, _) => Task.CompletedTask);

        await shell.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => sp.GetService<object>());
    }

    [Fact(DisplayName = "IShell does NOT expose public DisposeAsync / IAsyncDisposable / IDisposable (FR-037)")]
    public void IShell_DoesNot_Expose_PublicDispose()
    {
        var shellType = typeof(IShell);

        Assert.DoesNotContain(typeof(IAsyncDisposable), shellType.GetInterfaces());
        Assert.DoesNotContain(typeof(IDisposable), shellType.GetInterfaces());

        var dispose = shellType.GetMethod("DisposeAsync", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(dispose);
    }

    private static (Shell shell, ConcurrentQueue<(ShellLifecycleState, ShellLifecycleState)> events) CreateShell()
    {
        var events = new ConcurrentQueue<(ShellLifecycleState, ShellLifecycleState)>();
        var sp = new ServiceCollection().BuildServiceProvider();
        var shell = new Shell(Descriptor, sp, (_, prev, curr) =>
        {
            events.Enqueue((prev, curr));
            return Task.CompletedTask;
        });
        return (shell, events);
    }
}

file static class QueueExtensions
{
    public static void Clear<T>(this ConcurrentQueue<T> queue)
    {
        while (queue.TryDequeue(out _)) { }
    }
}
