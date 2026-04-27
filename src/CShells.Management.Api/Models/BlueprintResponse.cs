namespace CShells.Management.Api.Models;

/// <summary>
/// Registered-blueprint payload for a single shell. <c>ConfigurationData</c> is included
/// verbatim per FR-012a — values may contain host-controlled secrets, so authorization on the
/// install method (FR-014) is a hard prerequisite for non-localhost deployments.
/// </summary>
internal sealed record BlueprintResponse(
    string Name,
    IReadOnlyList<string> Features,
    IReadOnlyDictionary<string, object> ConfigurationData);
