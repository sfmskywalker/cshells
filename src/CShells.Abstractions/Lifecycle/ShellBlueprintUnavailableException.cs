namespace CShells.Lifecycle;

/// <summary>
/// Thrown when a blueprint-provider lookup fails transiently — typically because the underlying
/// store (database, blob container, etc.) is unreachable. The inner exception carries the
/// original fault; callers MAY retry once the source is healthy.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ShellBlueprintNotFoundException"/>: a "not found" reflects a clean
/// miss (the provider answered definitively that the name is unknown); an "unavailable"
/// reflects an inability to answer at all. The ASP.NET Core middleware translates the former
/// to <c>404</c> and the latter to <c>503</c>.
/// </remarks>
public sealed class ShellBlueprintUnavailableException : InvalidOperationException
{
    /// <summary>The shell name whose lookup failed.</summary>
    public string Name { get; }

    /// <summary>Initializes a new instance wrapping the provider's original fault.</summary>
    public ShellBlueprintUnavailableException(string name, Exception innerException)
        : base($"Blueprint lookup for shell '{Guard.Against.NullOrWhiteSpace(name)}' failed; " +
               "the underlying source is unavailable. See inner exception for details. The call " +
               "may be retried once the source is healthy again.",
               Guard.Against.Null(innerException))
    {
        Name = name;
    }
}
