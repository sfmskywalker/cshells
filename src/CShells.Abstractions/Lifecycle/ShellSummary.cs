namespace CShells.Lifecycle;

/// <summary>
/// Per-shell row returned by <see cref="IShellRegistry.ListAsync"/>. Left-joins the catalogue
/// (<see cref="BlueprintSummary"/> equivalent fields) with the registry's in-memory lifecycle
/// state at the moment the page is assembled.
/// </summary>
/// <remarks>
/// When there is no active shell for this name, <see cref="ActiveGeneration"/>,
/// <see cref="State"/>, and <see cref="LastScopeOpenedAt"/> are all <c>null</c>, and
/// <see cref="ActiveScopeCount"/> is <c>0</c>. When there is an active shell, all lifecycle
/// fields reflect its state at page-assembly time; subsequent mutations do not retroactively
/// update the returned page.
/// </remarks>
/// <param name="Name">Shell name. Non-empty.</param>
/// <param name="SourceId">Stable provider identifier.</param>
/// <param name="Mutable"><c>true</c> iff the owning provider has a manager.</param>
/// <param name="ActiveGeneration">Active generation number, or <c>null</c> when no active shell.</param>
/// <param name="State">Active shell's lifecycle state, or <c>null</c> when no active shell.</param>
/// <param name="ActiveScopeCount">Outstanding scope count on the active shell; <c>0</c> when inactive.</param>
/// <param name="LastScopeOpenedAt">
/// Timestamp of the most recent <c>BeginScope</c> on the active shell, or <c>null</c> when no
/// active shell or no scope has opened since activation.
/// </param>
/// <param name="Metadata">Provider-defined free-form key/value pairs.</param>
public sealed record ShellSummary(
    string Name,
    string SourceId,
    bool Mutable,
    int? ActiveGeneration,
    ShellLifecycleState? State,
    int ActiveScopeCount,
    DateTimeOffset? LastScopeOpenedAt,
    IReadOnlyDictionary<string, string> Metadata);
