using CShells.Features;

namespace CShells;

/// <summary>
/// Holds shell configuration including shell ID, enabled features, and shell-specific configuration.
/// </summary>
public class ShellSettings
{
    public ShellSettings() { }

    public ShellSettings(ShellId id) => Id = id;

    public ShellSettings(ShellId id, IReadOnlyList<string> enabledFeatures)
    {
        Guard.Against.Null(enabledFeatures);
        Id = id;
        EnabledFeatures = enabledFeatures;
    }

    /// <summary>
    /// Gets or initializes the shell identifier.
    /// </summary>
    public ShellId Id { get; init; }

    /// <summary>
    /// Gets or sets the list of enabled features for this shell.
    /// </summary>
    public IReadOnlyList<string> EnabledFeatures
    {
        get;
        set => field = [..value];
    } = [];

    /// <summary>
    /// Gets or sets shell-specific configuration data as a dictionary.
    /// Keys use colon-separated format for hierarchical data (e.g., "WebRouting:Path").
    /// </summary>
    public IDictionary<string, object> ConfigurationData { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Gets the code-first feature configurators registered via
    /// <c>WithFeature&lt;TFeature&gt;(Action&lt;TFeature&gt;)</c>.
    /// Each entry is keyed by feature name and holds a delegate that is applied to the
    /// live feature instance <em>after</em> configuration binding and <em>before</em>
    /// <c>ConfigureServices</c> is called, so code always wins over appsettings.
    /// </summary>
    /// <remarks>
    /// This collection is intentionally not serialized / persisted; it only exists in
    /// code-first shell registrations and is discarded once the shell context is built.
    /// </remarks>
    public IDictionary<string, Action<IShellFeature>> FeatureConfigurators { get; } = new Dictionary<string, Action<IShellFeature>>(StringComparer.OrdinalIgnoreCase);
}
