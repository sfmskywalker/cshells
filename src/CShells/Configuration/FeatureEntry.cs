namespace CShells.Configuration;

/// <summary>
/// Describes whether a configured feature entry enables or disables a feature.
/// </summary>
public enum FeatureEntryState
{
    /// <summary>
    /// The feature is enabled for the shell.
    /// </summary>
    Enabled,

    /// <summary>
    /// The feature is explicitly disabled for the shell.
    /// </summary>
    Disabled
}

/// <summary>
/// Represents a feature entry in shell configuration.
/// A feature entry can enable a feature, disable a feature, or enable a feature with direct settings.
/// </summary>
public class FeatureEntry
{
    /// <summary>
    /// Gets or sets the feature name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this entry enables or disables the feature.
    /// </summary>
    public FeatureEntryState State { get; set; } = FeatureEntryState.Enabled;

    /// <summary>
    /// Gets or sets a value indicating whether enabling this feature should reset lower-priority feature settings.
    /// </summary>
    public bool ResetsSettings { get; set; }

    /// <summary>
    /// Gets or sets the feature-specific settings.
    /// In object-map configuration, all child properties are treated as settings.
    /// </summary>
    public Dictionary<string, object?> Settings { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether this entry enables the feature.
    /// </summary>
    public bool IsEnabled => State == FeatureEntryState.Enabled;

    /// <summary>
    /// Creates a feature entry from just a feature name (no settings).
    /// </summary>
    /// <param name="name">The feature name.</param>
    /// <returns>A new <see cref="FeatureEntry"/> with only the name set.</returns>
    public static FeatureEntry FromName(string name) => new() { Name = name };

    /// <summary>
    /// Creates an enabled feature entry that resets inherited feature settings.
    /// </summary>
    /// <param name="name">The feature name.</param>
    /// <returns>A new enabled <see cref="FeatureEntry"/> with reset semantics.</returns>
    public static FeatureEntry EnableDefaults(string name) => new() { Name = name, ResetsSettings = true };

    /// <summary>
    /// Creates a disabled feature entry.
    /// </summary>
    /// <param name="name">The feature name.</param>
    /// <returns>A new disabled <see cref="FeatureEntry"/>.</returns>
    public static FeatureEntry Disabled(string name) => new() { Name = name, State = FeatureEntryState.Disabled };

    /// <summary>
    /// Returns the feature name.
    /// </summary>
    public override string ToString() => Name;
}
