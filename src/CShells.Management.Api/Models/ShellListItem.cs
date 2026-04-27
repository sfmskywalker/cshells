namespace CShells.Management.Api.Models;

/// <summary>
/// One catalogue entry in the list-shells response. <c>Active</c> is null when no generation
/// is currently active for that name; non-active generations (deactivating/draining/drained)
/// are not listed here — they're visible via the focused-view endpoint.
/// </summary>
internal sealed record ShellListItem(
    string Name,
    BlueprintResponse? Blueprint,
    ShellGenerationResponse? Active);
