namespace CShells.Configuration;

/// <summary>
/// Provides shell settings from a configurable source (e.g., configuration, database, blob storage).
/// </summary>
public interface IShellSettingsProvider
{
    /// <summary>
    /// Retrieves all shell settings asynchronously from the configured source.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A collection of shell settings.</returns>
    Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves shell settings for a specific shell by its identifier.
    /// </summary>
    /// <param name="shellId">The identifier of the shell to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The shell settings if found; otherwise, <c>null</c>.</returns>
    /// <remarks>
    /// Returning <c>null</c> means the provider does not currently define the requested shell.
    /// A <c>null</c> result is not an exceptional provider failure.
    /// </remarks>
    Task<ShellSettings?> GetShellSettingsAsync(ShellId shellId, CancellationToken cancellationToken = default);
}
