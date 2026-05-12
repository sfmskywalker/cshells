namespace CShells.Lifecycle;

/// <summary>
/// Thrown when shell initializer ordering metadata cannot produce a valid activation plan.
/// </summary>
public sealed class ShellInitializerOrderException : InvalidOperationException
{
    /// <summary>
    /// Creates a new <see cref="ShellInitializerOrderException"/> for the specified shell.
    /// </summary>
    /// <param name="shell">The shell descriptor being activated.</param>
    /// <param name="message">The ordering validation failure.</param>
    public ShellInitializerOrderException(ShellDescriptor shell, string message)
        : base($"Invalid shell initializer ordering for shell '{shell.Name}' generation {shell.Generation}: {message}")
    {
        Shell = shell;
    }

    /// <summary>
    /// Gets the shell descriptor whose initializer ordering failed validation.
    /// </summary>
    public ShellDescriptor Shell { get; }
}
