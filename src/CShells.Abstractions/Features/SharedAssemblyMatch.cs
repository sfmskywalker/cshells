namespace CShells.Features;

/// <summary>
/// Describes why an assembly simple name was selected as shared.
/// </summary>
/// <param name="AssemblyName">The assembly simple name that matched.</param>
/// <param name="SelectorKind">The kind of selector responsible for the match.</param>
/// <param name="SelectorPattern">The configured exact name or prefix pattern, when applicable.</param>
/// <param name="SelectorSource">The configuration path or code-first API source that contributed the selector.</param>
public sealed record SharedAssemblyMatch(
    string AssemblyName,
    SharedAssemblySelectorKind SelectorKind,
    string? SelectorPattern,
    string SelectorSource);
