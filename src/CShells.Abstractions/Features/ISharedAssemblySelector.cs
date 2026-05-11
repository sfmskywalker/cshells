namespace CShells.Features;

/// <summary>
/// Selects host assemblies that should be shared for shell feature discovery.
/// </summary>
/// <remarks>
/// Implementations are evaluated against assembly simple names only. They must not inspect
/// assembly full names, versions, cultures, public key tokens, file paths, or directory paths.
/// </remarks>
public interface ISharedAssemblySelector
{
    /// <summary>
    /// Gets a human-readable source that can be shown in diagnostics.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Attempts to match an assembly simple name.
    /// </summary>
    /// <param name="assemblyName">The assembly simple name to evaluate.</param>
    /// <param name="match">The match diagnostic when the selector matches.</param>
    /// <returns><see langword="true"/> when the assembly simple name is selected.</returns>
    bool TryMatch(string assemblyName, out SharedAssemblyMatch? match);
}
