namespace CShells.Lifecycle;

/// <summary>
/// Minimal per-blueprint row returned by <see cref="IShellBlueprintProvider.ListAsync"/>.
/// </summary>
/// <remarks>
/// Intentionally lightweight — constructing a full <see cref="IShellBlueprint"/> per row would
/// defeat paging at scale. Callers who need the full blueprint resolve it via
/// <see cref="IShellBlueprintProvider.GetAsync"/>.
/// </remarks>
/// <param name="Name">Shell name. Non-empty.</param>
/// <param name="SourceId">
/// Stable identifier of the provider that owns this blueprint. Typically the provider type's
/// short name. Callers treat this as opaque.
/// </param>
/// <param name="Mutable">
/// <c>true</c> iff the owning provider exposes an <see cref="IShellBlueprintManager"/> for this
/// name (i.e., create/update/delete are supported).
/// </param>
/// <param name="Metadata">Provider-defined free-form key/value pairs. May be empty.</param>
public sealed record BlueprintSummary(
    string Name,
    string SourceId,
    bool Mutable,
    IReadOnlyDictionary<string, string> Metadata);
