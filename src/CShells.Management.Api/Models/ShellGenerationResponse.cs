namespace CShells.Management.Api.Models;

/// <summary>
/// One generation entry in the focused-view response. The <c>Drain</c> field is populated
/// when the generation's lifecycle state is <c>Deactivating</c>, <c>Draining</c>, or
/// <c>Drained</c>.
/// </summary>
internal sealed record ShellGenerationResponse(
    int Generation,
    string State,
    DateTimeOffset CreatedAt,
    DrainSnapshot? Drain);
