namespace CShells.Management;

/// <summary>
/// Provides a queryable read model for the latest desired shell definitions and the currently applied runtimes.
/// </summary>
public interface IShellRuntimeStateAccessor
{
    /// <summary>
    /// Gets the runtime status for every configured shell.
    /// </summary>
    IReadOnlyCollection<ShellRuntimeStatus> GetAllShells();

    /// <summary>
    /// Gets the runtime status for a specific shell.
    /// </summary>
    /// <param name="shellId">The shell identifier.</param>
    /// <returns>The current runtime status for the shell, or <see langword="null"/> when the shell is not configured.</returns>
    ShellRuntimeStatus? GetShell(ShellId shellId);
}

