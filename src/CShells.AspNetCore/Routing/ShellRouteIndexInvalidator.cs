using CShells.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.AspNetCore.Routing;

/// <summary>
/// Subscribes to <see cref="IShellRegistry"/> lifecycle events and invalidates the
/// <see cref="DefaultShellRouteIndex"/> snapshot whenever a transition suggests routing
/// metadata may have changed.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle subscriber registration happens in
/// <see cref="ServiceCollectionExtensions"/> after the registry singleton is constructed —
/// the invalidator registers itself with the registry via <see cref="IShellRegistry.Subscribe"/>
/// inside its <see cref="StartAsync"/> hosted-service hook so it observes every transition
/// from process start.
/// </para>
/// <para>
/// We invalidate on transitions to <see cref="ShellLifecycleState.Initializing"/> (a new
/// generation is starting — routing config may have changed via reload, or a brand-new
/// blueprint was added) and to <see cref="ShellLifecycleState.Disposed"/> (a generation has
/// gone away — a blueprint may have been removed via the management API). Other transitions
/// do not change routing metadata and are ignored.
/// </para>
/// <para>
/// Subscriber-isolation per Constitution Principle VII: if invalidation throws, we log and
/// swallow so we never block the registry's notification fan-out for other subscribers.
/// </para>
/// </remarks>
internal sealed class ShellRouteIndexInvalidator(
    IShellRouteIndex routeIndex,
    ILogger<ShellRouteIndexInvalidator>? logger = null) : IShellLifecycleSubscriber
{
    private readonly IShellRouteIndex _routeIndex = Guard.Against.Null(routeIndex);
    private readonly ILogger<ShellRouteIndexInvalidator> _logger = logger ?? NullLogger<ShellRouteIndexInvalidator>.Instance;

    /// <inheritdoc />
    public Task OnStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default)
    {
        if (current is not (ShellLifecycleState.Initializing or ShellLifecycleState.Disposed))
            return Task.CompletedTask;

        try
        {
            if (_routeIndex is DefaultShellRouteIndex defaultIndex)
            {
                defaultIndex.Invalidate();
                _logger.LogDebug(
                    "Invalidated route-index snapshot in response to '{ShellName}' transition {Previous} → {Current}.",
                    shell.Descriptor.Name, previous, current);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Route-index invalidation threw for '{ShellName}' ({Previous} → {Current}); swallowing per subscriber-isolation contract.",
                shell.Descriptor.Name, previous, current);
        }

        return Task.CompletedTask;
    }
}
