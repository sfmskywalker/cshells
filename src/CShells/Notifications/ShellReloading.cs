namespace CShells.Notifications;

/// <summary>
/// Notification published when a shell reload operation is starting.
/// </summary>
/// <param name="ShellId">
/// The ID of the shell being reloaded, or <c>null</c> for an aggregate (full) reload operation.
/// </param>
/// <remarks>
/// <para>This notification is always emitted before any other notifications during a reload operation.</para>
/// <para>For single-shell reload: emitted once with the target <see cref="ShellId"/>.</para>
/// <para>For full reload: emitted once with <c>null</c> <see cref="ShellId"/> as the aggregate start,
/// and once per changed shell with the individual <see cref="ShellId"/>.</para>
/// </remarks>
public record ShellReloading(ShellId? ShellId) : INotification;
