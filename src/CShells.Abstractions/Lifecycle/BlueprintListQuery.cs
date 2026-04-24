namespace CShells.Lifecycle;

/// <summary>
/// Paging and filtering input for <see cref="IShellBlueprintProvider.ListAsync"/>.
/// </summary>
/// <param name="Cursor">
/// Opaque continuation token returned by a previous <see cref="BlueprintPage.NextCursor"/>.
/// <c>null</c> requests the first page.
/// </param>
/// <param name="Limit">
/// Maximum entries to return in a single page. Range: <c>[1, 500]</c>. Guarded.
/// </param>
/// <param name="NamePrefix">
/// Optional case-insensitive ordinal prefix filter. <c>null</c> returns all names.
/// </param>
public sealed record BlueprintListQuery(
    string? Cursor = null,
    int Limit = 50,
    string? NamePrefix = null)
{
    /// <summary>Minimum allowed page size.</summary>
    public const int MinLimit = 1;

    /// <summary>Maximum allowed page size.</summary>
    public const int MaxLimit = 500;

    /// <summary>
    /// Validates <see cref="Limit"/> is within <c>[MinLimit, MaxLimit]</c>. Called by
    /// providers at the public entry point.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="Limit"/> is outside <c>[1, 500]</c>.
    /// </exception>
    public void EnsureValid()
    {
        if (Limit < MinLimit || Limit > MaxLimit)
            throw new ArgumentOutOfRangeException(
                nameof(Limit), Limit, $"Limit must be between {MinLimit} and {MaxLimit}.");
    }
}
