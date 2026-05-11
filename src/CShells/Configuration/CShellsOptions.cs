namespace CShells.Configuration;

/// <summary>
/// Root configuration model for CShells section in appsettings.json.
/// </summary>
public class CShellsOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "CShells";

    /// <summary>
    /// Gets or sets shell configurations keyed by shell name.
    /// </summary>
    public Dictionary<string, ShellConfig> Shells { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets host-wide shared assembly simple-name selectors.
    /// </summary>
    public List<string> SharedAssemblies { get; set; } = [];
}
