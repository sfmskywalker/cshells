# Runtime Shell Management

CShells supports adding, updating, removing, and reloading shells at runtime without restarting the application. Under the deferred-activation model, runtime management now records **desired** shell definitions immediately and reconciles them into **applied** runtimes only when a candidate runtime is fully ready.

---

## `IShellManager`

`IShellManager` is registered automatically by `AddShells()`. Inject it into any service that needs to manage shells at runtime.

```csharp
public class TenantProvisioningService
{
    private readonly IShellManager shellManager;

    public TenantProvisioningService(IShellManager shellManager)
    {
        this.shellManager = shellManager;
    }
}
```

---

## Adding a Shell

```csharp
public async Task CreateTenantAsync(string tenantId, string tier)
{
    var features = tier switch
    {
        "enterprise" => new[] { "Core", "Billing", "Reporting", "FraudDetection" },
        "pro"        => new[] { "Core", "Billing", "Reporting" },
        _            => new[] { "Core", "Billing" }
    };

    var settings = new ShellSettings(new ShellId(tenantId), features);
    settings.ConfigurationData["WebRouting:Path"] = tenantId;

    await shellManager.AddShellAsync(settings);
}
```

When a shell is added:
1. Its settings are recorded as the new **desired** definition.
2. CShells refreshes the runtime feature catalog.
3. If the candidate runtime builds successfully, it is committed and becomes active.
4. If the shell cannot be applied yet, the desired state remains recorded and visible through runtime status until a later reconciliation succeeds.

---

## Removing a Shell

```csharp
public async Task DeleteTenantAsync(string tenantId)
{
    await shellManager.RemoveShellAsync(new ShellId(tenantId));
}
```

Removal is the explicit operation that deletes a shell from desired state and tears down its applied runtime.

---

## Updating a Shell

```csharp
public async Task UpgradeTenantAsync(string tenantId, string newTier)
{
    var features = newTier == "enterprise"
        ? new[] { "Core", "Billing", "Reporting", "FraudDetection" }
        : new[] { "Core", "Billing" };

    var settings = new ShellSettings(new ShellId(tenantId), features);
    settings.ConfigurationData["WebRouting:Path"] = tenantId;

    await shellManager.UpdateShellAsync(settings);
}
```

`UpdateShellAsync` no longer tears down the current runtime first. It records a newer desired generation and attempts to reconcile it into a successor runtime. If the new desired generation is deferred or fails, the last-known-good applied runtime stays routable.

---

## Reloading All Shells

```csharp
public async Task RefreshAllTenantsAsync()
{
    await shellManager.ReloadAllShellsAsync();
}
```

Every full reload:
- refreshes the runtime feature catalog first
- reloads the latest desired shell set from providers
- commits only shells whose successor runtimes are fully ready
- preserves already applied shells when newer desired generations defer or fail

---

## Reloading a Single Shell

```csharp
public async Task RefreshTenantAsync(string tenantId)
{
    await shellManager.ReloadShellAsync(new ShellId(tenantId));
}
```

- The shell records the latest desired definition from the provider before reconciliation.
- The previous applied runtime remains available until a successor commits.
- Unrelated applied shells are not affected.
- If the shell is unknown to the provider, the call throws without mutating runtime state.

---

## Inspecting Desired vs. Applied State

Inject `IShellRuntimeStateAccessor` when you need to distinguish configured intent from currently serving runtimes.

```csharp
public class ShellStatusService(IShellRuntimeStateAccessor runtimeState)
{
    public IReadOnlyCollection<ShellRuntimeStatus> GetShells() => runtimeState.GetAllShells();
}
```

Each `ShellRuntimeStatus` reports:
- the latest `DesiredGeneration`
- the committed `AppliedGeneration` (if any)
- whether the shell is currently `IsInSync`
- whether it is currently `IsRoutable`
- any `BlockingReason` / `MissingFeatures`

This means operators can see configured-but-unapplied shells without those shells becoming routable or endpoint-visible.

---

## Shell Lifecycle Notifications

CShells publishes notifications during shell lifecycle events.

### Available Notifications

| Notification | When |
|---|---|
| `ShellActivated` | A candidate runtime has been committed and is now applied |
| `ShellDeactivating` | An applied runtime is about to be replaced or removed |
| `ShellAdded` | A shell was added via `IShellManager.AddShellAsync` |
| `ShellRemoved` | A shell was removed via `IShellManager.RemoveShellAsync` |
| `ShellUpdated` | A shell was updated via `IShellManager.UpdateShellAsync` |
| `ShellReloading` | A reload operation is starting |
| `ShellReloaded` | A reload operation completed successfully |
| `ShellsReloaded` | A reconciliation pass produced a fresh runtime status snapshot |

### Reload Notification Ordering

During a **single-shell reload** (`ReloadShellAsync`):
1. `ShellReloading(shellId)`
2. Desired state is refreshed from the provider
3. The runtime feature catalog is refreshed
4. If a successor commits, `ShellDeactivating` / `ShellActivated` are emitted around the applied runtime swap
5. `ShellReloaded(shellId, [shellId], statuses)`

During a **full reload** (`ReloadAllShellsAsync`):
1. `ShellReloading(null)`
2. Desired state is refreshed from providers
3. The runtime feature catalog is refreshed
4. Ready candidates commit individually; deferred/failed generations remain unapplied
5. `ShellsReloaded(statuses)`
6. `ShellReloaded(null, changedShells, statuses)`

---

## `IShellHost` — Accessing Applied Shells

Inject `IShellHost` to enumerate or look up applied shell contexts.

```csharp
public class ShellDashboardService
{
    private readonly IShellHost shellHost;

    public ShellDashboardService(IShellHost shellHost)
    {
        this.shellHost = shellHost;
    }

    public IEnumerable<string> GetActiveShellNames() =>
        shellHost.AllShells.Select(shell => shell.Settings.Id.Value);

    public ShellContext GetShell(string name) =>
        shellHost.GetShell(new ShellId(name));
}
```

| Member | Description |
|---|---|
| `IShellHost.AllShells` | All currently applied shell contexts |
| `IShellHost.DefaultShell` | The explicit `"Default"` shell only when it is applied; otherwise the first applied shell is used only when no explicit default exists |
| `IShellHost.GetShell(ShellId)` | Looks up an applied shell by ID; throws `KeyNotFoundException` if no committed runtime exists |
