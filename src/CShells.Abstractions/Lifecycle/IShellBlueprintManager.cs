namespace CShells.Lifecycle;

/// <summary>
/// Optional write-side peer of an <see cref="IShellBlueprintProvider"/> whose underlying
/// source accepts mutation. Persists create / update / delete operations; the registry
/// independently drives runtime cleanup (drain + dispose) after <see cref="DeleteAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Providers wrapping read-only sources (configuration files, code-seeded registrations) do
/// NOT implement this interface. Attempting to mutate a read-only-owned blueprint raises
/// <see cref="BlueprintNotMutableException"/>.
/// </para>
/// <para>
/// Implementations MUST persist changes to the underlying store before returning from any
/// mutating operation — callers (notably <see cref="IShellRegistry.UnregisterBlueprintAsync"/>)
/// sequence subsequent work after the persisted state is committed.
/// </para>
/// <para>
/// The registry discovers manager association via the <see cref="ProvidedBlueprint.Manager"/>
/// attached by the owning provider; it does NOT scan registered managers independently. The
/// <see cref="Owns"/> predicate exists for direct callers (e.g., a future admin API) that hold
/// a manager reference and need to route writes without a provider lookup.
/// </para>
/// </remarks>
public interface IShellBlueprintManager
{
    /// <summary>
    /// Fast predicate: does this manager claim ownership of <paramref name="name"/>?
    /// Sub-millisecond — no I/O. Typically a prefix match or a cached set membership check.
    /// </summary>
    bool Owns(string name);

    /// <summary>
    /// Persists a new blueprint derived from <paramref name="settings"/>. The settings'
    /// <c>Id.Name</c> is the key.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a blueprint for the given name already exists in the underlying store.
    /// </exception>
    Task CreateAsync(ShellSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the persisted blueprint for <paramref name="settings"/>.Id.Name. The running
    /// shell is NOT reloaded by this call — callers reload explicitly via
    /// <see cref="IShellRegistry.ReloadAsync"/> when ready.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no blueprint exists for the given name in the underlying store.
    /// </exception>
    Task UpdateAsync(ShellSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the persisted blueprint for <paramref name="name"/>. Does NOT touch the
    /// registry's runtime state — the registry drains and disposes the active generation
    /// itself as part of <see cref="IShellRegistry.UnregisterBlueprintAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no blueprint exists for the given name in the underlying store.
    /// </exception>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
