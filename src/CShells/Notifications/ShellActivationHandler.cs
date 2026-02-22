using CShells.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Notifications;

/// <summary>
/// Handles shell activation by invoking all registered <see cref="IShellActivatedHandler"/> instances.
/// </summary>
public class ShellActivationHandler : INotificationHandler<ShellActivated>
{
    private readonly ILogger<ShellActivationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellActivationHandler"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public ShellActivationHandler(ILogger<ShellActivationHandler>? logger = null)
    {
        _logger = logger ?? NullLogger<ShellActivationHandler>.Instance;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ShellActivated notification, CancellationToken cancellationToken = default)
    {
        var shellId = notification.Context.Id;
        _logger.LogInformation("Activating services for shell '{ShellId}'", shellId);

        var handlers = notification.Context.ServiceProvider
            .GetServices<IShellActivatedHandler>()
            .OrderForActivation()
            .ToList();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.OnActivatedAsync(cancellationToken);
                _logger.LogDebug("Activated handler '{HandlerType}' for shell '{ShellId}'",
                    handler.GetType().Name, shellId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to activate handler '{HandlerType}' for shell '{ShellId}'",
                    handler.GetType().Name, shellId);
                throw;
            }
        }

        _logger.LogInformation("Successfully activated {HandlerCount} handler(s) for shell '{ShellId}'",
            handlers.Count, shellId);
    }
}
