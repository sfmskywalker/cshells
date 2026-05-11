namespace CShells.Features;

/// <summary>
/// Identifies the kind of selector that chose a shared assembly.
/// </summary>
public enum SharedAssemblySelectorKind
{
    /// <summary>
    /// The selector matched one assembly simple name exactly.
    /// </summary>
    Exact,

    /// <summary>
    /// The selector matched assembly simple names by prefix.
    /// </summary>
    PrefixPattern,

    /// <summary>
    /// The selector used code-first predicate logic.
    /// </summary>
    Predicate
}
