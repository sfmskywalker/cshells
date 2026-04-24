namespace CShells.Lifecycle;

/// <summary>
/// Thrown when a blueprint lookup returns <c>null</c> — no provider in the composite claims
/// the requested name.
/// </summary>
public sealed class ShellBlueprintNotFoundException : InvalidOperationException
{
    /// <summary>The shell name that was not found.</summary>
    public string Name { get; }

    /// <summary>Initializes a new instance for the given name.</summary>
    public ShellBlueprintNotFoundException(string name)
        : base($"No blueprint is registered for shell '{Guard.Against.NullOrWhiteSpace(name)}'. " +
               "Check the composite provider's registered sources, or create one via an " +
               "IShellBlueprintManager if the host has a mutable source.")
    {
        Name = name;
    }
}
