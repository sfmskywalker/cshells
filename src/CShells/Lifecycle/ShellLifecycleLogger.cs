using CShells.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Lifecycle;

/// <summary>
/// Default <see cref="IShellLifecycleSubscriber"/> that emits structured log entries for
/// every shell lifecycle transition.
/// </summary>
/// <remarks>
/// Auto-registered and auto-subscribed by <c>AddCShells</c>; no host configuration required.
/// Event IDs live in the reserved range 1000–1099 (1000–1009 for transitions, 1010–1019 for
/// drain warnings). Required properties on every entry: <c>ShellName</c>, <c>Generation</c>,
/// <c>PreviousState</c>, <c>CurrentState</c>.
/// </remarks>
internal sealed class ShellLifecycleLogger : IShellLifecycleSubscriber
{
    private static readonly EventId StateTransitionEvent = new(1000, nameof(StateTransitionEvent));

    private readonly ILogger<ShellLifecycleLogger> _logger;

    public ShellLifecycleLogger(ILogger<ShellLifecycleLogger>? logger = null)
    {
        _logger = logger ?? NullLogger<ShellLifecycleLogger>.Instance;
    }

    /// <inheritdoc />
    public Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken = default)
    {
        _logger.Log(
            logLevel: LogLevel.Information,
            eventId: StateTransitionEvent,
            state: new ShellLifecycleLogState(shell.Descriptor.Name, shell.Descriptor.Generation, previous, current),
            exception: null,
            formatter: static (s, _) => s.ToString());

        return Task.CompletedTask;
    }
}

/// <summary>
/// Structured log state for shell lifecycle transitions. Exposes <c>ShellName</c>,
/// <c>Generation</c>, <c>PreviousState</c>, and <c>CurrentState</c> as enumerable key-value
/// pairs so structured sinks can surface them.
/// </summary>
internal readonly struct ShellLifecycleLogState(string shellName, int generation, ShellLifecycleState previous, ShellLifecycleState current)
    : IReadOnlyList<KeyValuePair<string, object?>>
{
    public string ShellName { get; } = shellName;
    public int Generation { get; } = generation;
    public ShellLifecycleState PreviousState { get; } = previous;
    public ShellLifecycleState CurrentState { get; } = current;

    public int Count => 4;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new("ShellName", ShellName),
        1 => new("Generation", Generation),
        2 => new("PreviousState", PreviousState.ToString()),
        3 => new("CurrentState", CurrentState.ToString()),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"Shell {ShellName}#{Generation} transitioned {PreviousState} → {CurrentState}";
}
