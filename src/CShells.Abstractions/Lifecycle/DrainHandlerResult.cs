namespace CShells.Lifecycle;

/// <summary>Outcome for a single <see cref="IDrainHandler"/> invocation.</summary>
/// <param name="HandlerTypeName">The concrete handler type's <c>.Name</c>.</param>
/// <param name="Completed">True if the handler returned within the deadline.</param>
/// <param name="Elapsed">Wall-clock time consumed by this handler.</param>
/// <param name="Error">Non-null if the handler threw.</param>
public sealed record DrainHandlerResult(
    string HandlerTypeName,
    bool Completed,
    TimeSpan Elapsed,
    Exception? Error);
