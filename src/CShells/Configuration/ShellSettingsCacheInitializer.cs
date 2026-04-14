using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CShells.Configuration;

/// <summary>
/// Background service that loads <see cref="ShellSettings"/> into the <see cref="ShellSettingsCache"/>
/// at application startup without activating shells.
/// </summary>
public class ShellSettingsCacheInitializer(
    IShellSettingsProvider provider,
    IShellSettingsCache cache,
    ILogger<ShellSettingsCacheInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Loading shell settings into cache...");

        try
        {
            var settings = await provider.GetShellSettingsAsync(cancellationToken);
            var settingsList = settings.ToList();
            cache.Load(settingsList);

            logger.LogInformation("Loaded {Count} shell setting(s) into cache", settingsList.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load shell settings into cache");
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
