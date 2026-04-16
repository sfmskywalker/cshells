using CShells.Management;

namespace CShells.Notifications;

/// <summary>
/// Notification published when a shell reload operation has completed successfully.
/// </summary>
/// <param name="ShellId">
/// The ID of the shell that was reloaded, or <c>null</c> for an aggregate (full) reload operation.
/// </param>
/// <param name="ChangedShells">
/// The IDs of shells whose reconciliation outcome was Added, Updated, or Removed.
/// For single-shell reload, this contains only the reloaded shell.
/// For full reload, this contains all shells that changed during reconciliation.
/// </param>
/// <param name="Statuses">The current runtime status projection after the reconciliation pass completes.</param>
/// <remarks>
/// <para>This notification is always emitted last, only on successful completion.</para>
/// <para>Failed reload operations do not emit this notification.</para>
/// <para>For full reload: the existing aggregate <see cref="ShellsReloaded"/> notification is emitted
/// before this notification.</para>
/// </remarks>
public record ShellReloaded(
    ShellId? ShellId,
    IReadOnlyCollection<ShellId> ChangedShells,
    IReadOnlyCollection<ShellRuntimeStatus> Statuses) : INotification;
