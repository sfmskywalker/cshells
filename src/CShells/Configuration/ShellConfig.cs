using System.Text.Json.Serialization;

namespace CShells.Configuration;

/// <summary>
/// Configuration model for a shell section in appsettings.json.
/// </summary>
public class ShellConfig
{
    /// <summary>
    /// Gets or sets the name of the shell.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of enabled features for this shell.
    /// Accepts two JSON shapes:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Array form</b> — each entry is a string or <c>{ "Name": "…", … }</c> object.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Object-map form</b> — each property key is the feature name and its value is a settings object
    ///     (use <c>{}</c> for features with no settings).
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
    ///   "Core": {},
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
