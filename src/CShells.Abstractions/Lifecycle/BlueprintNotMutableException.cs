namespace CShells.Lifecycle;

/// <summary>
/// Thrown when a mutation operation (create / update / delete / unregister) targets a
/// blueprint whose owning provider has no <see cref="IShellBlueprintManager"/> — typically
/// because the source is a configuration file or code-seeded registration.
/// </summary>
public sealed class BlueprintNotMutableException : InvalidOperationException
{
    /// <summary>The shell name whose source is read-only.</summary>
    public string Name { get; }

    /// <summary>The owning provider's <see cref="BlueprintSummary.SourceId"/>, when known.</summary>
    public string? SourceId { get; }

    /// <summary>Initializes a new instance for the given name.</summary>
    public BlueprintNotMutableException(string name, string? sourceId = null)
        : base(BuildMessage(Guard.Against.NullOrWhiteSpace(name), sourceId))
    {
        Name = name;
        SourceId = sourceId;
    }

    private static string BuildMessage(string name, string? sourceId) =>
        sourceId is null
            ? $"Blueprint '{name}' has no registered manager; its source is read-only. Register " +
              $"an {nameof(IShellBlueprintManager)} for this name's owning provider to enable mutation."
            : $"Blueprint '{name}' is owned by '{sourceId}', which is read-only. Register an " +
              $"{nameof(IShellBlueprintManager)} for '{sourceId}' to enable mutation.";
}
