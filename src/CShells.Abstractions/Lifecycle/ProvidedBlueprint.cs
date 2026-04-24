namespace CShells.Lifecycle;

/// <summary>
/// A blueprint paired with the manager that owns its underlying source, if any.
/// </summary>
/// <remarks>
/// Returned by <see cref="IShellBlueprintProvider.GetAsync"/>. A non-null <paramref name="Manager"/>
/// indicates the blueprint's source accepts mutation; a null <paramref name="Manager"/> indicates
/// a read-only source (e.g., a configuration-file provider, or a code-seeded blueprint).
/// </remarks>
/// <param name="Blueprint">The blueprint. Required.</param>
/// <param name="Manager">
/// The owning manager when the source is mutable, otherwise <c>null</c>. When non-null,
/// <see cref="IShellBlueprintManager.Owns"/> for <paramref name="Blueprint"/>.<see cref="IShellBlueprint.Name"/>
/// MUST return <c>true</c>.
/// </param>
public sealed record ProvidedBlueprint(IShellBlueprint Blueprint, IShellBlueprintManager? Manager = null);
