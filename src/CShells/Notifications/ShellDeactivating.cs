using CShells.Hosting;

namespace CShells.Notifications;

/// <summary>
/// Notification published when a shell is about to be deactivated and removed.
/// Published BEFORE the shell is removed from cache and BEFORE its service provider is disposed.
/// </summary>
public record ShellDeactivating(ShellContext Context) : INotification;
