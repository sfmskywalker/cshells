using CShells.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CShells.Configuration;

/// <summary>
/// Background service that initializes the <see cref="ShellSettingsCache"/> at application startup
/// by loading shell settings from an <see cref="IShellSettingsProvider"/>.
/// </summary>
public class ShellSettingsCacheInitializer(IShellManager shellManager, ILogger<ShellSettingsCacheInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Loading shell settings into cache...");

        try
        {
            await shellManager.ReloadAllShellsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load shell settings into cache");
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
