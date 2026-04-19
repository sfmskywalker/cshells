namespace CShells.Configuration;

/// <summary>
/// Decorator that applies <see cref="ShellBuilder"/>-based configurators to every
/// <see cref="ShellSettings"/> returned by the inner provider.
/// </summary>
internal sealed class ConfiguringShellSettingsProvider(
    IShellSettingsProvider inner,
    IReadOnlyList<Action<ShellBuilder>> configurators) : IShellSettingsProvider
{
    public async Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await inner.GetShellSettingsAsync(cancellationToken);
        return settings.Select(ApplyConfigurators);
    }

    public async Task<ShellSettings?> GetShellSettingsAsync(ShellId shellId, CancellationToken cancellationToken = default)
    {
        var settings = await inner.GetShellSettingsAsync(shellId, cancellationToken);
        return settings is not null ? ApplyConfigurators(settings) : null;
    }

    private ShellSettings ApplyConfigurators(ShellSettings settings)
    {
        var builder = new ShellBuilder(settings);
        foreach (var configurator in configurators)
            configurator(builder);
        return settings;
    }
}
