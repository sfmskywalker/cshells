namespace CShells.Lifecycle;

/// <summary>
/// An async seam for contributing shell blueprints that cannot be discovered synchronously
/// during DI setup — e.g., blueprints loaded from a remote blob store, a database, or any
/// other I/O-bound source.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are registered via <c>IServiceCollection</c> (e.g.,
/// <c>services.AddSingleton&lt;IShellBlueprintProvider, MyProvider&gt;()</c>). The built-in
/// startup hosted service enumerates every registered provider, awaits
/// <see cref="GetBlueprintsAsync"/>, calls <see cref="IShellRegistry.RegisterBlueprint"/> for
/// each returned blueprint, and only then activates.
/// </para>
/// <para>
/// This keeps the DI-registration phase non-blocking: no sync-over-async on startup, no
/// potential deadlocks in environments with a synchronization context, no blocking I/O at
/// container-build time.
/// </para>
/// </remarks>
public interface IShellBlueprintProvider
{
    /// <summary>
    /// Returns the blueprints this provider contributes. Called once at host start, after DI
    /// setup is complete and the registry has been constructed.
    /// </summary>
    Task<IReadOnlyList<IShellBlueprint>> GetBlueprintsAsync(CancellationToken cancellationToken = default);
}
