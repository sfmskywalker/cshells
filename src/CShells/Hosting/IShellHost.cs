namespace CShells.Hosting;

/// <summary>
/// Provides access to shell contexts and their configurations.
/// </summary>
public interface IShellHost
{
    /// <summary>
    /// Gets the default shell context. Returns the shell with Id "Default" if present,
    /// otherwise returns the first shell.
    /// </summary>
    ShellContext DefaultShell { get; }

    /// <summary>
    /// Gets a shell context by its identifier.
    /// </summary>
    /// <param name="id">The shell identifier.</param>
    /// <returns>The shell context for the specified identifier.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no shell with the specified identifier exists.</exception>
    ShellContext GetShell(ShellId id);

    /// <summary>
    /// Gets all available shell contexts.
    /// </summary>
    IReadOnlyCollection<ShellContext> AllShells { get; }

    /// <summary>
    /// Evicts the cached context for the specified shell, disposing its service provider.
    /// The next access to this shell will rebuild it from the latest shell settings.
    /// </summary>
    /// <param name="shellId">The shell whose cached context should be evicted.</param>
    ValueTask EvictShellAsync(ShellId shellId);

    /// <summary>
    /// Evicts all cached shell contexts, disposing their service providers.
    /// The next access to any shell will rebuild it from the latest shell settings.
    /// </summary>
    ValueTask EvictAllShellsAsync();
}
