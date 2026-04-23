namespace CShells.Lifecycle;

/// <summary>
/// Per-shell holder that lets services inside a shell's provider resolve <see cref="IShell"/>.
/// The registry constructs the holder before building the provider (so it can be registered
/// as a singleton) and populates the shell reference once the shell has been fully built.
/// </summary>
internal sealed class ShellHolder
{
    private IShell? _shell;

    /// <summary>The owning shell. Throws if resolved before <see cref="Set"/> is called.</summary>
    public IShell Shell => Volatile.Read(ref _shell)
        ?? throw new InvalidOperationException(
            "IShell is not yet available — a service resolved IShell before the shell's construction completed. " +
            "Resolve shell-owned services only after the shell has transitioned to Active.");

    internal void Set(IShell shell) => Volatile.Write(ref _shell, Guard.Against.Null(shell));
}
