namespace CShells.Hosting;

/// <summary>
/// Ensures a shell host has completed any deferred initialization required before shells can be accessed.
/// </summary>
public interface IShellHostInitializer
{
    /// <summary>
    /// Ensures the shell host is ready to serve shell contexts.
    /// Implementations must be safe to call multiple times.
    /// </summary>
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}

