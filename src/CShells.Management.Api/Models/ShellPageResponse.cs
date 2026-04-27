namespace CShells.Management.Api.Models;

/// <summary>Paginated catalogue response from the list-shells endpoint.</summary>
internal sealed record ShellPageResponse(
    IReadOnlyList<ShellListItem> Items,
    string? NextCursor,
    int PageSize);
