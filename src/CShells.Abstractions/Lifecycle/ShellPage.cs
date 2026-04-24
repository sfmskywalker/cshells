namespace CShells.Lifecycle;

/// <summary>
/// A page of shell entries returned by <see cref="IShellRegistry.ListAsync"/>.
/// </summary>
/// <param name="Items">Entries in the current page. May be empty.</param>
/// <param name="NextCursor">
/// Opaque continuation token for the next page, or <c>null</c> when the catalogue has been
/// fully enumerated.
/// </param>
public sealed record ShellPage(
    IReadOnlyList<ShellSummary> Items,
    string? NextCursor);
