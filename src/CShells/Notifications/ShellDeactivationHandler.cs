using CShells.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Notifications;

/// <summary>
/// Handles shell deactivation by invoking all registered <see cref="IShellDeactivatingHandler"/> instances.
/// </summary>
/// <remarks>
/// Handlers are invoked in reverse registration order (LIFO) to mirror typical dependency shutdown patterns.
/// Exceptions during deactivation are logged but do not prevent other handlers from running.
/// </remarks>
public class ShellDeactivationHandler : INotificationHandler<ShellDeactivating>
{
    private readonly ILogger<ShellDeactivationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellDeactivationHandler"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public ShellDeactivationHandler(ILogger<ShellDeactivationHandler>? logger = null)
    {
        _logger = logger ?? NullLogger<ShellDeactivationHandler>.Instance;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ShellDeactivating notification, CancellationToken cancellationToken = default)
    {
        var shellId = notification.Context.Id;
        _logger.LogInformation("Deactivating services for shell '{ShellId}'", shellId);

        var handlers = notification.Context.ServiceProvider
            .GetServices<IShellDeactivatingHandler>()
            .Reverse(); // Deactivate in reverse order of registration

        var handlerCount = 0;
        var failedCount = 0;

        foreach (var handler in handlers)
        {
            handlerCount++;
            try
            {
                await handler.OnDeactivatingAsync(cancellationToken);
                _logger.LogDebug("Deactivated handler '{HandlerType}' for shell '{ShellId}'",
                    handler.GetType().Name, shellId);
            }
            catch (Exception ex)
            {
                failedCount++;
                // Log but don't throw - we want to attempt deactivation of all handlers
                _logger.LogError(ex,
                    "Failed to deactivate handler '{HandlerType}' for shell '{ShellId}'",
                    handler.GetType().Name, shellId);
            }
        }

        if (failedCount > 0)
        {
            _logger.LogWarning("Deactivated {SuccessCount}/{TotalCount} handler(s) for shell '{ShellId}' ({FailedCount} failed)",
                handlerCount - failedCount, handlerCount, shellId, failedCount);
        }
        else
        {
            _logger.LogInformation("Successfully deactivated {HandlerCount} handler(s) for shell '{ShellId}'",
                handlerCount, shellId);
        }
    }
}
