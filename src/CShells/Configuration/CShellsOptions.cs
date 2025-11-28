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
    /// Gets or sets the collection of shell configurations.
    /// </summary>
    public List<ShellConfig> Shells { get; set; } = [];
}
