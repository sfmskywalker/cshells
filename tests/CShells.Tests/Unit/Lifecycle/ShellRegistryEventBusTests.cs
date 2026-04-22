using System.Collections.Concurrent;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Unit.Lifecycle;

/// <summary>
/// Tests the subscriber fan-out behaviour on <see cref="ShellRegistry"/> — the part of US8
/// that every downstream phase depends on.
/// </summary>
public class ShellRegistryEventBusTests
{
    [Fact(DisplayName = "Subscribers receive every transition")]
    public async Task Subscribers_ReceiveEveryTransition()
    {
        var registry = new ShellRegistry();
        var subscriber = new RecordingSubscriber();
        registry.Subscribe(subscriber);
        var shell = CreateShellAttachedTo(registry);

        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        await shell.TryTransitionAsync(ShellLifecycleState.Active, ShellLifecycleState.Deactivating);

        Assert.Equal(
            [
                (ShellLifecycleState.Initializing, ShellLifecycleState.Active),
                (ShellLifecycleState.Active, ShellLifecycleState.Deactivating),
            ],
            subscriber.Events);
    }

    [Fact(DisplayName = "Unsubscribe stops event delivery")]
    public async Task Unsubscribe_StopsDelivery()
    {
        var registry = new ShellRegistry();
        var subscriber = new RecordingSubscriber();
        registry.Subscribe(subscriber);
        var shell = CreateShellAttachedTo(registry);

        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        registry.Unsubscribe(subscriber);
        await shell.TryTransitionAsync(ShellLifecycleState.Active, ShellLifecycleState.Deactivating);

        Assert.Single(subscriber.Events);
    }

    [Fact(DisplayName = "Throwing subscriber does not abort other subscribers or the transition")]
    public async Task ThrowingSubscriber_DoesNotAbortOthers()
    {
        var logs = new CollectingLoggerProvider();
        var logger = new LoggerFactory([logs]).CreateLogger<ShellRegistry>();
        var registry = new ShellRegistry(logger);

        registry.Subscribe(new ThrowingSubscriber("first"));
        var good = new RecordingSubscriber();
        registry.Subscribe(good);
        registry.Subscribe(new ThrowingSubscriber("last"));

        var shell = CreateShellAttachedTo(registry);
        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);

        Assert.Single(good.Events);
        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(2, logs.Entries.Count(e => e.LogLevel == LogLevel.Error));
    }

    [Fact(DisplayName = "Duplicate subscribe is idempotent")]
    public async Task Subscribe_Duplicate_IsIdempotent()
    {
        var registry = new ShellRegistry();
        var subscriber = new RecordingSubscriber();
        registry.Subscribe(subscriber);
        registry.Subscribe(subscriber);

        var shell = CreateShellAttachedTo(registry);
        await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active);

        Assert.Single(subscriber.Events);
    }

    [Fact(DisplayName = "Blueprint registration: duplicate name throws")]
    public void RegisterBlueprint_DuplicateName_Throws()
    {
        var registry = new ShellRegistry();
        registry.RegisterBlueprint(new FakeBlueprint("payments"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterBlueprint(new FakeBlueprint("Payments"))); // case-insensitive

        Assert.Contains("payments", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "GetBlueprintNames returns every registered name")]
    public void GetBlueprintNames_ReturnsAll()
    {
        var registry = new ShellRegistry();
        registry.RegisterBlueprint(new FakeBlueprint("a"));
        registry.RegisterBlueprint(new FakeBlueprint("b"));

        Assert.Equal(["a", "b"], registry.GetBlueprintNames().OrderBy(n => n));
    }

    private static Shell CreateShellAttachedTo(ShellRegistry registry)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var descriptor = ShellDescriptor.Create("test", 1);
        return new Shell(descriptor, sp, (shell, prev, curr) =>
            registry.FireStateChangedAsync(shell, prev, curr));
    }

    private sealed class RecordingSubscriber : IShellLifecycleSubscriber
    {
        public ConcurrentQueue<(ShellLifecycleState, ShellLifecycleState)> Events { get; } = new();

        public Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken = default)
        {
            Events.Enqueue((previous, current));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSubscriber(string label) : IShellLifecycleSubscriber
    {
        public Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"subscriber '{label}' threw");
    }

    private sealed class FakeBlueprint(string name) : IShellBlueprint
    {
        public string Name { get; } = name;
        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();
        public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ShellSettings(new ShellId(Name)));
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CollectingLogger(Entries);
        public void Dispose() { }

        private sealed class CollectingLogger(List<LogEntry> entries) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }

    internal sealed record LogEntry(LogLevel LogLevel, EventId EventId, string Message, Exception? Exception);
}
