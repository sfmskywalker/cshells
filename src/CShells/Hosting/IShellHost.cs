namespace CShells.Hosting;

/// <summary>
/// Provides access to committed applied shell runtimes.
/// </summary>
public interface IShellHost
{
    /// <summary>
    /// Gets the default applied shell context.
    /// Returns the shell with Id "Default" when it is explicitly configured and currently applied;
    /// otherwise returns the first applied shell when no explicit default exists.
    /// </summary>
    ShellContext DefaultShell { get; }

    /// <summary>
    /// Gets a shell context by its identifier.
    /// </summary>
    /// <param name="id">The shell identifier.</param>
    /// <returns>The applied shell context for the specified identifier.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no committed applied runtime exists for the specified shell.</exception>
    ShellContext GetShell(ShellId id);

    /// <summary>
    /// Gets all committed applied shell contexts.
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

    /// <summary>
    /// Acquires a scope tracking handle for the specified shell context.
    /// The handle must be disposed when the scope ends so that the host can safely defer disposal
    /// of shells that are replaced while request scopes are still active.
    /// </summary>
    IAsyncDisposable AcquireContextScope(ShellContext context);
}
