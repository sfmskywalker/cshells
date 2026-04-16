# Public Contract: Shell Runtime Reconciliation

## Runtime Management Contract

The runtime management surface continues to manage shell configuration changes and reconciliation, but its behavioral contract changes to use desired-state recording plus applied-runtime reconciliation instead of immediate in-place activation.

This public interface belongs in `CShells.Abstractions`, even though its implementation stays in `CShells`.

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

- `AddShellAsync(settings)` records a new desired generation for the shell and then runs reconciliation for that shell.
- `UpdateShellAsync(settings)` records a newer desired generation and then runs reconciliation for that shell.
- `ReloadShellAsync(shellId)` refreshes the runtime feature catalog first, obtains the latest desired definition for that shell from the configured provider, records it as desired state, and reconciles the shell using the same candidate-build semantics as startup and full reload.
- `ReloadAllShellsAsync()` refreshes the runtime feature catalog first, obtains the latest desired shell set from the configured provider, records those desired generations, and reconciles every configured shell independently.
- If a shell's latest desired generation is `DeferredDueToMissingFeatures` or `Failed`, the command still succeeds as a reconciliation operation provided the catalog refresh itself succeeded and the desired state was recorded.
- `RemoveShellAsync(shellId)` is the explicit operation that removes a shell from desired state and tears down its applied runtime if one exists.

## Runtime State Inspection Contract

Operators and runtime components need a queryable read model for desired-vs-applied shell truth.

This public interface belongs in `CShells.Abstractions`.

```csharp
namespace CShells.Management;

public interface IShellRuntimeStateAccessor
{
    IReadOnlyCollection<ShellRuntimeStatus> GetAllShells();
    ShellRuntimeStatus? GetShell(ShellId shellId);
}

public enum ShellReconciliationOutcome
{
    Active,
    DeferredDueToMissingFeatures,
    Failed
}

public sealed record ShellRuntimeStatus(
    ShellId ShellId,
    long DesiredGeneration,
    long? AppliedGeneration,
    ShellReconciliationOutcome Outcome,
    bool IsInSync,
    bool IsRoutable,
    string? BlockingReason,
    IReadOnlyCollection<string> MissingFeatures);
```

### Behavioral Rules

- Every configured shell appears in `GetAllShells()`, even when it has no applied runtime.
- `DesiredGeneration` is always the latest configured generation for the shell.
- `AppliedGeneration` is null when no runtime has ever been committed or when the shell has been explicitly removed/deactivated.
- `Outcome == Active` means the shell has an applied runtime, but `IsInSync` may still be false if the shell is serving a last-known-good runtime while a newer desired generation is deferred or failed.
- `BlockingReason` explains why the latest desired generation is not currently applied.
- `MissingFeatures` is populated only when the latest desired generation is deferred because the committed catalog does not contain required features.

## Applied Runtime Hosting Contract

The hosting layer continues to expose runtime shell contexts, but only committed applied runtimes are eligible.

Current host implementation remains in `CShells` and does not require a new consumer-extensibility contract for the internal candidate-build pipeline.

### Behavioral Rules

- `IShellHost.GetShell(shellId)` succeeds only for shells with a committed applied runtime.
- `IShellHost.AllShells` returns only committed applied runtimes.
- `IShellHost.DefaultShell` returns the explicitly configured `Default` shell only when it currently has an applied runtime.
- If `Default` is explicitly configured but currently unapplied, default resolution reports it unavailable instead of silently returning another shell.
- If no explicit `Default` shell is configured, fallback selection may choose only from shells with applied active runtimes.

## Lifecycle and Reconciliation Notification Contract

Framework-owned notifications remain in `CShells`, but their behavioral meaning changes to align with applied-runtime commits.

```csharp
namespace CShells.Notifications;

public record ShellActivated(/* committed applied runtime */) : INotification;
public record ShellDeactivating(/* currently applied runtime being replaced or removed */) : INotification;
public record ShellReloading(/* reconciliation scope metadata */) : INotification;
public record ShellReloaded(/* reconciliation result metadata */) : INotification;
public record ShellsReloaded(/* aggregate reconciliation snapshot */) : INotification;
```

### Behavioral Rules

- `ShellActivated` is emitted only when a candidate runtime is committed and becomes applied.
- Recording a new desired generation that later defers or fails does not emit `ShellActivated`.
- `ShellDeactivating` is emitted only for a currently applied runtime that is about to be replaced or removed.
- Desired-state events such as `ShellAdded`, `ShellUpdated`, and `ShellRemoved` describe configuration intent changes and must not by themselves imply endpoint eligibility.
- Aggregate reconciliation notifications must give consumers enough information to distinguish active, deferred, and failed shells after a reconciliation pass.
- Endpoint registration and routing consumers must respond only to applied-runtime results, not to desired-state updates alone.

