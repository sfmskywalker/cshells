namespace CShells.Lifecycle;

/// <summary>
/// Paging and filtering input for <see cref="IShellRegistry.ListAsync"/>.
/// </summary>
/// <remarks>
/// Superset of <see cref="BlueprintListQuery"/> adding <see cref="StateFilter"/>. When
/// <see cref="StateFilter"/> is non-null, only active shells in the specified state are
/// returned; inactive blueprints are filtered out.
/// </remarks>
/// <param name="Cursor">Opaque continuation token. <c>null</c> requests the first page.</param>
/// <param name="Limit">Maximum entries per page. Range: <c>[1, 500]</c>.</param>
/// <param name="NamePrefix">Optional case-insensitive ordinal prefix filter.</param>
/// <param name="StateFilter">
/// Optional lifecycle-state filter. When set, only entries whose active shell is in the given
/// state are returned. Inactive blueprints are excluded.
/// <para>
/// The filter is applied AFTER fetching a page from the underlying catalogue, so filtered
/// pages may return fewer than <see cref="Limit"/> items (or zero) while still carrying a
/// non-null <see cref="ShellPage.NextCursor"/>. Callers MUST continue iterating until
/// <see cref="ShellPage.NextCursor"/> is <c>null</c>, not stop when a page is empty or
/// undersized.
/// </para>
/// </param>
public sealed record ShellListQuery(
    string? Cursor = null,
    int Limit = 50,
    string? NamePrefix = null,
    ShellLifecycleState? StateFilter = null)
{
    /// <summary>Minimum allowed page size.</summary>
    public const int MinLimit = 1;

    /// <summary>Maximum allowed page size.</summary>
    public const int MaxLimit = 500;

    /// <summary>Validates <see cref="Limit"/> is within <c>[MinLimit, MaxLimit]</c>.</summary>
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
