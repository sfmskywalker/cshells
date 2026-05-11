namespace CShells.Lifecycle;

/// <summary>
/// Thrown when shell initializer ordering metadata cannot produce a valid activation plan.
/// </summary>
internal sealed class ShellInitializerOrderException : InvalidOperationException
{
    public ShellInitializerOrderException(ShellDescriptor shell, string message)
        : base($"Invalid shell initializer ordering for shell '{shell.Name}' generation {shell.Generation}: {message}")
    {
        Shell = shell;
    }

    public ShellDescriptor Shell { get; }
}
