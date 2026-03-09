namespace CShells.Management;

/// <summary>
/// Provides methods for managing shells at runtime.
/// Supports adding, removing, updating, and reloading shells without requiring application restart.
/// </summary>
public interface IShellManager
{
    /// <summary>
    /// Adds a new shell to the system.
    /// </summary>
    /// <param name="settings">The shell settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the shell has been added and activated.</returns>
    Task AddShellAsync(ShellSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a shell from the system.
    /// </summary>
    /// <param name="shellId">The ID of the shell to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the shell has been removed.</returns>
    Task RemoveShellAsync(ShellId shellId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing shell's configuration.
    /// </summary>
    /// <param name="settings">The updated shell settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the shell has been updated.</returns>
    Task UpdateShellAsync(ShellSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a single shell from the configured shell settings provider.
    /// </summary>
    /// <param name="shellId">The ID of the shell to reload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the shell has been reloaded.</returns>
    /// <remarks>
    /// <para>
    /// This method uses strict reload semantics:
    /// </para>
    /// <list type="bullet">
    /// <item>If the provider returns a shell definition for the specified ID, the runtime refreshes
    /// that shell and makes the refreshed state effective on the next access.</item>
    /// <item>If the provider returns <c>null</c> for the specified ID, the operation fails explicitly
    /// and does not delete or mutate the current runtime state for that shell.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the provider does not define the requested shell.
    /// </exception>
    Task ReloadShellAsync(ShellId shellId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads all shells from the configured shell settings provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when all shells have been reloaded.</returns>
    /// <remarks>
    /// This method reconciles runtime shell membership to provider state by adding new shells,
    /// updating changed shells, preserving unchanged shells, and removing shells no longer returned.
    /// </remarks>
    Task ReloadAllShellsAsync(CancellationToken cancellationToken = default);
}
