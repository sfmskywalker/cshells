# Public Contract: Shell Reload Semantics

## Runtime Management Contract

The runtime management surface adds targeted shell reload while preserving existing add, remove, update, and full reload capabilities.

This public interface belongs in `CShells.Abstractions`, even though it keeps the `CShells.Management` namespace.

Backward compatibility is not required for this feature, so the public contract may be changed directly rather than introduced as a compatibility-preserving alternative.

```csharp
namespace CShells.Management;

public interface IShellManager
{
    Task AddShellAsync(ShellSettings settings, CancellationToken cancellationToken = default);
    Task RemoveShellAsync(ShellId shellId, CancellationToken cancellationToken = default);
    Task UpdateShellAsync(ShellSettings settings, CancellationToken cancellationToken = default);
    Task ReloadShellAsync(ShellId shellId, CancellationToken cancellationToken = default);
    Task ReloadAllShellsAsync(CancellationToken cancellationToken = default);
}
```

### Behavioral Rules

- `ReloadShellAsync(shellId)` is strict.
- If the provider returns a shell definition for `shellId`, the runtime refreshes that shell and makes the refreshed state effective on the next access.
- If the provider returns `null` for `shellId`, the operation fails explicitly and does not delete or mutate the current runtime state for that shell.
- `ReloadAllShellsAsync()` reconciles runtime shell membership to provider state by adding new shells, updating changed shells, preserving unchanged shells, and removing shells no longer returned.

## Provider Lookup Contract

Providers must expose targeted lookup in addition to full enumeration.

This public interface belongs in `CShells.Abstractions`, even though it keeps the `CShells.Configuration` namespace.

```csharp
namespace CShells.Configuration;

public interface IShellSettingsProvider
{
    Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default);
    Task<ShellSettings?> GetShellSettingsAsync(ShellId shellId, CancellationToken cancellationToken = default);
}
```

### Behavioral Rules

- The shell-specific overload does not require callers to enumerate all shells.
- Returning `null` means the provider does not currently define the requested shell.
- Provider lookup absence is not an exceptional provider failure.

## Hosting Cache Invalidation Contract

The host must expose an internal framework seam that allows the manager or hosting infrastructure to invalidate affected cached `ShellContext` instances without forcing eager rebuilds.

### Behavioral Rules

- Invalidating a cached shell context disposes the old runtime context if one exists.
- Invalidating a shell does not eagerly rebuild it.
- The next runtime access rebuilds from the latest shell settings.
- Full reload invalidates all affected cached shell contexts needed to prevent stale service-provider reuse.
- This seam does not need to be part of the public `IShellHost` contract.

## Notification Contract

Reload-specific notifications supplement existing lifecycle notifications.

These notification records remain in `CShells` as framework-owned public message types consumed through the existing notification abstractions, which is allowed by the constitution's notification-record exception.

```csharp
namespace CShells.Notifications;

public record ShellReloading(/* scope metadata, target shell metadata */) : INotification;
public record ShellReloaded(/* scope metadata, target shell metadata, changed shell metadata */) : INotification;
```

### Behavioral Rules

- `ShellReloading` is emitted first.
- Existing lifecycle notifications are emitted in their normal order as applicable.
- Full reload continues to emit the existing aggregate `ShellsReloaded` notification, and that notification is preserved rather than replaced.
- For full reload, the existing aggregate `ShellsReloaded` notification is emitted before the final aggregate `ShellReloaded` notification.
- `ShellReloaded` is emitted last and only on successful completion.
- Full reload emits one aggregate reload-start notification, one aggregate reload-complete notification, and per-shell reload notifications for each changed shell.
- A changed shell is any shell whose reconciliation outcome is Added, Updated, or Removed.
- Unchanged shells do not emit per-shell reload notifications.
- Failed reload operations do not emit a successful `ShellReloaded` notification.
