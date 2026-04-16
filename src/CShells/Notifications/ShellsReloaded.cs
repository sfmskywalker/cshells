using CShells.Management;

namespace CShells.Notifications;

/// <summary>
/// Notification published when the full shell set has been synchronized,
/// such as after a full reload or after startup activation completes.
/// </summary>
/// <param name="Statuses">The runtime status projection for all configured shells after synchronization.</param>
public record ShellsReloaded(IReadOnlyCollection<ShellRuntimeStatus> Statuses) : INotification;
