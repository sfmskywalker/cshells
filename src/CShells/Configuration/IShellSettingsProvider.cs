namespace CShells.Configuration;

/// <summary>
/// Provides shell settings from a configurable source (e.g., configuration, database, blob storage).
/// </summary>
public interface IShellSettingsProvider
{
    /// <summary>
    /// Retrieves shell settings asynchronously from the configured source.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A collection of shell settings.</returns>
    Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default);
}
