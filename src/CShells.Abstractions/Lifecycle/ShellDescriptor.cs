using System.Collections.Immutable;

namespace CShells.Lifecycle;

/// <summary>
/// Immutable identity + metadata snapshot for a single shell generation.
/// </summary>
/// <param name="Name">Shell name.</param>
/// <param name="Generation">Library-assigned monotonic generation number, starting at 1.</param>
/// <param name="CreatedAt">UTC timestamp at shell creation.</param>
/// <param name="Metadata">Opaque metadata copied from the blueprint at generation time.</param>
/// <remarks>
/// Formats as <c>"{Name}#{Generation}"</c> for use in structured log fields.
/// </remarks>
public sealed record ShellDescriptor(
    string Name,
    int Generation,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>
    /// Creates a descriptor with empty metadata and the current UTC timestamp.
    /// </summary>
    public static ShellDescriptor Create(string name, int generation, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Guard.Against.NullOrWhiteSpace(name),
            generation >= 1 ? generation : throw new ArgumentOutOfRangeException(nameof(generation), "Generation must be >= 1."),
            DateTimeOffset.UtcNow,
            metadata ?? ImmutableDictionary<string, string>.Empty);

    /// <inheritdoc />
    public override string ToString() => $"{Name}#{Generation}";
}
