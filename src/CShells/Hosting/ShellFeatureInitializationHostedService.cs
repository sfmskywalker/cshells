using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Hosting;

/// <summary>
/// Ensures deferred shell feature discovery completes before shell startup activates any shells.
/// </summary>
public class ShellFeatureInitializationHostedService : IHostedService
{
    private readonly DefaultShellHost _shellHost;
    private readonly ILogger<ShellFeatureInitializationHostedService> _logger;

    public ShellFeatureInitializationHostedService(
        DefaultShellHost shellHost,
        ILogger<ShellFeatureInitializationHostedService>? logger = null)
    {
        _shellHost = Guard.Against.Null(shellHost);
        _logger = logger ?? NullLogger<ShellFeatureInitializationHostedService>.Instance;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing shell feature discovery");
        await _shellHost.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
