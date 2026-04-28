using CShells.Lifecycle;
using Microsoft.Extensions.Hosting;

namespace CShells.AspNetCore.Routing;

/// <summary>
/// Subscribes <see cref="ShellRouteIndexInvalidator"/> to <see cref="IShellRegistry"/> on host
/// start and unsubscribes on stop. Required because <see cref="IShellRegistry.Subscribe"/> is
/// imperative — DI alone does not wire subscribers.
/// </summary>
internal sealed class ShellRouteIndexInvalidatorHostedService(
    IShellRegistry registry,
    ShellRouteIndexInvalidator invalidator) : IHostedService
{
    private readonly IShellRegistry _registry = Guard.Against.Null(registry);
    private readonly ShellRouteIndexInvalidator _invalidator = Guard.Against.Null(invalidator);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Subscribe(_invalidator);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _registry.Unsubscribe(_invalidator);
        return Task.CompletedTask;
    }
}
