# Shell Lifecycle

CShells lifecycle handlers run against **applied** shell runtimes, not merely against configured intent.

## Desired vs. Applied

Deferred activation introduces two layers of truth per shell:

- **Desired state** — the latest recorded `ShellSettings`
- **Applied runtime** — the last committed shell runtime that is safe to serve

A shell can be configured without currently being applied.

## Lifecycle Handler Rules

| Handler | Runs when |
|---|---|
| `IShellActivatedHandler` | A candidate runtime is committed and becomes applied |
| `IShellDeactivatingHandler` | An applied runtime is about to be replaced or removed |

That means:

- recording a new desired generation does **not** trigger activation by itself
- deferred / failed desired generations do **not** deactivate the last-known-good applied runtime
- reload deactivation happens only when a successor runtime is ready to commit

## Startup Behavior

During application startup, CShells:
1. records desired state for configured shells
2. refreshes the runtime feature catalog
3. reconciles shells into applied runtimes where possible
4. publishes runtime status for all configured shells

Only shells with applied runtimes become routable.

## Runtime Notifications

Framework notifications follow applied-runtime transitions:

- `ShellActivated` — emitted when a candidate runtime commits
- `ShellDeactivating` — emitted when an applied runtime is being replaced or removed
- `ShellsReloaded` / `ShellReloaded` — emitted after reconciliation with runtime status snapshots

## Inspecting State

Use `IShellRuntimeStateAccessor` to inspect every configured shell, including shells that are deferred or failed:

```csharp
public class ShellStatusPage(IShellRuntimeStateAccessor runtimeState)
{
    public IReadOnlyCollection<ShellRuntimeStatus> GetShells() => runtimeState.GetAllShells();
}
```

This lets operators distinguish:

- shells that are configured and applied
- shells that are configured but deferred
- shells that are serving a last-known-good runtime while a newer desired generation is blocked

