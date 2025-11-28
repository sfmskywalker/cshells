namespace CShells;

/// <summary>
/// An attribute that defines a feature's metadata for shell startup classes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShellFeatureAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShellFeatureAttribute"/> class with the specified name.
    /// </summary>
    /// <param name="name">The name of the feature.</param>
    public ShellFeatureAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the name of the feature.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the feature names that this feature depends on.
    /// </summary>
    public string[] DependsOn { get; set; } = [];

    /// <summary>
    /// Gets or sets the metadata associated with this feature.
    /// Note: Attribute properties cannot use Dictionary types directly. Use named properties for simple metadata.
    /// </summary>
    public object[] Metadata { get; set; } = [];
}
