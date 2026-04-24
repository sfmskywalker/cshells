namespace CShells.Lifecycle;

/// <summary>
/// Thrown by <see cref="IShellBlueprintProvider"/> implementations (typically the composite)
/// when two sub-providers claim ownership of the same shell name.
/// </summary>
/// <remarks>
/// Each shell name MUST be owned by exactly one provider. The composite detects duplicates
/// lazily — at first colliding lookup or listing — rather than at composition time, because
/// exhaustively scanning every provider at startup defeats the scale-ready goals of feature
/// <c>007</c>.
/// </remarks>
public sealed class DuplicateBlueprintException : InvalidOperationException
{
    /// <summary>The conflicting shell name.</summary>
    public string Name { get; }

    /// <summary>The first provider observed to claim the name.</summary>
    public Type FirstProviderType { get; }

    /// <summary>The second provider observed to claim the name.</summary>
    public Type SecondProviderType { get; }

    /// <summary>Initializes a new instance describing the collision.</summary>
    public DuplicateBlueprintException(string name, Type firstProviderType, Type secondProviderType)
        : base($"Shell name '{Guard.Against.NullOrWhiteSpace(name)}' is claimed by both " +
               $"'{Guard.Against.Null(firstProviderType).Name}' and " +
               $"'{Guard.Against.Null(secondProviderType).Name}'. " +
               "Each shell name must be owned by exactly one blueprint provider.")
    {
        Name = name;
        FirstProviderType = firstProviderType;
        SecondProviderType = secondProviderType;
    }
}
