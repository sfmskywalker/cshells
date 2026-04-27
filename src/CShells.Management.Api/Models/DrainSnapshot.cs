namespace CShells.Management.Api.Models;

/// <summary>
/// Point-in-time snapshot of an in-flight drain operation, derived from
/// <see cref="CShells.Lifecycle.IDrainOperation"/>'s <c>Status</c> and <c>Deadline</c>.
/// </summary>
internal sealed record DrainSnapshot(string Status, DateTimeOffset? Deadline);
