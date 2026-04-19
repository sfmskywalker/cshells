namespace CShells.Configuration;

/// <summary>
/// Decorator that applies <see cref="ShellBuilder"/>-based configurators to every
/// <see cref="ShellSettings"/> returned by the inner provider.
/// </summary>
internal sealed class ConfiguringShellSettingsProvider(
    IShellSettingsProvider inner,
    IReadOnlyList<Action<ShellBuilder>> configurators) : IShellSettingsProvider
{
    private readonly IShellSettingsProvider inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Action<ShellBuilder>[] configurators = (configurators ?? throw new ArgumentNullException(nameof(configurators))).ToArray();
    public async Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await inner.GetShellSettingsAsync(cancellationToken);
        return settings.Select(ApplyConfigurators).ToArray();
    }

    public async Task<ShellSettings?> GetShellSettingsAsync(ShellId shellId, CancellationToken cancellationToken = default)
    {
        var settings = await inner.GetShellSettingsAsync(shellId, cancellationToken);
        return settings is not null ? ApplyConfigurators(settings) : null;
    }

    private ShellSettings ApplyConfigurators(ShellSettings settings)
    {
        var clone = CloneSettings(settings);
        var builder = new ShellBuilder(clone);
        foreach (var configurator in configurators)
            configurator(builder);
        return clone;
    }

    private static ShellSettings CloneSettings(ShellSettings settings)
    {
        var clone = new ShellSettings(settings.Id)
        {
            EnabledFeatures = settings.EnabledFeatures,
            ConfigurationData = new Dictionary<string, object>(settings.ConfigurationData),
        };

        foreach (var (key, value) in settings.FeatureConfigurators)
            clone.FeatureConfigurators[key] = value;

        return clone;
    }
}
