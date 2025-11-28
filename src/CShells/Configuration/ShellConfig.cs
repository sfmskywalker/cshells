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
    /// </summary>
    public string[] Features { get; set; } = [];

    /// <summary>
    /// Gets or sets arbitrary properties associated with this shell.
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();
}
