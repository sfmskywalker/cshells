using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Hosting;

/// <summary>
/// Ensures deferred shell feature discovery completes before shell startup activates any shells.
/// </summary>
public class ShellFeatureInitializationHostedService : IHostedService
{
    private readonly IShellHostInitializer _shellHostInitializer;
    private readonly ILogger<ShellFeatureInitializationHostedService> _logger;

    public ShellFeatureInitializationHostedService(
        IShellHostInitializer shellHostInitializer,
        ILogger<ShellFeatureInitializationHostedService>? logger = null)
    {
        _shellHostInitializer = Guard.Against.Null(shellHostInitializer);
        _logger = logger ?? NullLogger<ShellFeatureInitializationHostedService>.Instance;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing shell feature discovery");
        await _shellHostInitializer.EnsureInitializedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
