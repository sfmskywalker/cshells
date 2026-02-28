# Runtime Shell Management

CShells supports adding, updating, and removing shells at runtime without restarting the application. Use `IShellManager` for programmatic shell lifecycle management.

---

## `IShellManager`

`IShellManager` is registered automatically by `AddShells()`. Inject it into any service that needs to manage shells at runtime.

```csharp
public class TenantProvisioningService
{
    private readonly IShellManager _shellManager;

    public TenantProvisioningService(IShellManager shellManager)
    {
        _shellManager = shellManager;
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

    // Optionally set routing and feature configuration
    settings.ConfigurationData["WebRouting:Path"] = tenantId;

    await _shellManager.AddShellAsync(settings);
    // Shell is now active and handling requests
}
```

When a shell is added:
1. Its settings are stored in the cache.
2. Its DI container is built from the enabled features.
3. Its endpoints are registered with the routing system (for web features).

---

## Removing a Shell

```csharp
public async Task DeleteTenantAsync(string tenantId)
{
    await _shellManager.RemoveShellAsync(new ShellId(tenantId));
    // Shell is immediately removed; its DI container is disposed
}
```

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

    await _shellManager.UpdateShellAsync(settings);
    // Old shell is removed and new shell is added atomically
}
```

`UpdateShellAsync` removes the existing shell and adds the new one. There may be a brief period during which requests to the affected shell return 404.

---

## Reloading All Shells

Reload all shells from the configured providers without restarting the application. Useful when shell configurations are stored externally (e.g., in a database or blob storage) and have changed.

```csharp
public async Task RefreshAllTenantsAsync()
{
    await _shellManager.ReloadAllShellsAsync();
}
```

Shells that no longer exist in the providers are removed; new shells are added; changed shells are updated.

---

## Shell Lifecycle Notifications

CShells publishes notifications during shell lifecycle events. Register a handler to react to them.

### Available Notifications

| Notification | When |
|---|---|
| `ShellActivated` | A shell's DI container has been built and is ready |
| `ShellDeactivating` | A shell is about to be shut down |
| `ShellAdded` | A shell was added via `IShellManager.AddShellAsync` |
| `ShellRemoved` | A shell was removed via `IShellManager.RemoveShellAsync` |
| `ShellUpdated` | A shell was updated via `IShellManager.UpdateShellAsync` |
| `ShellsReloaded` | All shells were reloaded via `IShellManager.ReloadAllShellsAsync` |

### Implementing a Notification Handler

```csharp
using CShells.Notifications;

public class TenantActivatedHandler : INotificationHandler<ShellActivated>
{
    private readonly ILogger<TenantActivatedHandler> _logger;

    public TenantActivatedHandler(ILogger<TenantActivatedHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(ShellActivated notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shell '{ShellId}' activated with {FeatureCount} features",
            notification.Context.Settings.Id.Value,
            notification.Context.Settings.EnabledFeatures.Count);

        return Task.CompletedTask;
    }
}
```

Register it in the DI container:

```csharp
builder.Services.AddSingleton<INotificationHandler<ShellActivated>, TenantActivatedHandler>();
```

### `IShellActivatedHandler` and `IShellDeactivatingHandler`

Register these in a feature's `ConfigureServices` to react to shell lifecycle events from within the shell's own DI container:

```csharp
using CShells.Hosting;
using Microsoft.Extensions.DependencyInjection;

public class AnalyticsActivationHandler : IShellActivatedHandler, IShellDeactivatingHandler
{
    private readonly IAnalyticsCollector _collector;

    public AnalyticsActivationHandler(IAnalyticsCollector collector)
    {
        _collector = collector;
    }

    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        _collector.StartTracking();
        return Task.CompletedTask;
    }

    public Task OnDeactivatingAsync(CancellationToken cancellationToken = default)
    {
        _collector.Flush();
        return Task.CompletedTask;
    }
}

// Register in the feature:
[ShellFeature("Analytics")]
public class AnalyticsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAnalyticsCollector, AnalyticsCollector>();
        services.AddSingleton<IShellActivatedHandler, AnalyticsActivationHandler>();
        services.AddSingleton<IShellDeactivatingHandler, AnalyticsActivationHandler>();
    }
}
```

Because these handlers are registered in the shell's DI container, they can depend on any shell-scoped service.

---

## `IShellHost` — Accessing Shells

Inject `IShellHost` to enumerate or look up active shell contexts.

```csharp
public class ShellDashboardService
{
    private readonly IShellHost _shellHost;

    public ShellDashboardService(IShellHost shellHost)
    {
        _shellHost = shellHost;
    }

    public IEnumerable<string> GetActiveShellNames() =>
        _shellHost.AllShells.Select(s => s.Settings.Id.Value);

    public ShellContext GetShell(string name) =>
        _shellHost.GetShell(new ShellId(name));
}
```

| Member | Description |
|---|---|
| `IShellHost.AllShells` | All currently active shell contexts |
| `IShellHost.DefaultShell` | The shell named `"Default"`, or the first shell |
| `IShellHost.GetShell(ShellId)` | Look up a shell by ID; throws `KeyNotFoundException` if not found |
