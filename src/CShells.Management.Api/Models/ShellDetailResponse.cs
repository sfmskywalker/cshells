namespace CShells.Management.Api.Models;

/// <summary>
/// Focused-view response for a single shell. <c>Generations</c> includes the active generation
/// and every still-held deactivating/draining/drained generation; each entry's
/// <see cref="ShellGenerationResponse.Drain"/> snapshot makes drain progress visible inline.
/// </summary>
internal sealed record ShellDetailResponse(
    string Name,
    BlueprintResponse? Blueprint,
    IReadOnlyList<ShellGenerationResponse> Generations);
