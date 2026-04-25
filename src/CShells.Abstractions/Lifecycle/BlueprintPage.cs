namespace CShells.Lifecycle;

/// <summary>
/// A page of catalogue entries returned by <see cref="IShellBlueprintProvider.ListAsync"/>.
/// </summary>
/// <param name="Items">Entries in the current page. May be empty. <c>Items.Count &lt;= Limit</c>.</param>
/// <param name="NextCursor">
/// Opaque continuation token for the next page, or <c>null</c> when the catalogue has been
/// fully enumerated. Callers pass this back via
/// <see cref="BlueprintListQuery.Cursor"/> to resume.
/// </param>
public sealed record BlueprintPage(
    IReadOnlyList<BlueprintSummary> Items,
    string? NextCursor);
