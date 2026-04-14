namespace CShells.Notifications;

/// <summary>
/// Notification published when the full shell set has been synchronized,
/// such as after a full reload or after startup activation completes.
/// </summary>
/// <param name="AllShells">All shell settings after synchronization.</param>
public record ShellsReloaded(IReadOnlyCollection<ShellSettings> AllShells) : INotification;
