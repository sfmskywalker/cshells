using CShells.Hosting;

namespace CShells.Notifications;

/// <summary>
/// Notification published when a shell has been activated and its service provider is ready.
/// </summary>
public record ShellActivated(ShellContext Context) : INotification;
