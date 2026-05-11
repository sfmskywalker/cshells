using System.Text.Json.Serialization;

namespace CShells.Configuration;

/// <summary>
/// Configuration model for a single shell definition.
/// </summary>
public class ShellConfig
{
    /// <summary>
    /// Gets or sets the optional shell name used by standalone shell configuration providers.
    /// Root <c>CShells:Shells</c> configuration derives shell identity from the parent map key.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the configured features for this shell.
    /// The preferred JSON shape is an object map whose property keys are feature names and whose values
    /// are <c>true</c>, <c>false</c>, or direct feature settings objects.
    /// Legacy array entries are still normalized to the same runtime model.
    /// Accepted JSON shapes:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Array form</b> — each entry is a string or <c>{ "Name": "…", … }</c> object.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Object-map form</b> — each property key is the feature name and its value is <c>true</c> to
    ///     enable with defaults, <c>false</c> to disable, or an object for direct feature settings.
    ///   </description></item>
    /// </list>
    /// Both forms normalize to the same runtime <see cref="FeatureEntry"/> list.
    /// </summary>
    /// <example>
    /// Array form:
    /// <code>
    /// "Features": [
    ///   "Core",
    ///   { "Name": "Analytics", "TopPostsCount": 10 }
    /// ]
    /// </code>
    /// Object-map form:
    /// <code>
    /// "Features": {
    ///   "Core": true,
    ///   "LegacyAuth": false,
    ///   "Analytics": { "TopPostsCount": 10 }
    /// }
    /// </code>
    /// </example>
    [JsonConverter(typeof(FeatureEntryListJsonConverter))]
    public List<FeatureEntry> Features { get; set; } = [];

    /// <summary>
    /// Gets or sets shell-specific configuration.
    /// These settings are available via IConfiguration in the shell's service provider.
    /// </summary>
    public Dictionary<string, object?> Configuration { get; set; } = new();
}
