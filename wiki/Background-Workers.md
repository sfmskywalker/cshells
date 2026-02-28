# Background Workers

CShells provides `IShellContextScopeFactory` to execute work within the context of a specific shell. Use it in background services, scheduled jobs, or any non-HTTP workload that needs access to shell-scoped services.

---

## `IShellContextScopeFactory`

`IShellContextScopeFactory` creates a `IShellContextScope` — a lightweight scope wrapping a shell's `IServiceProvider`.

```csharp
public interface IShellContextScopeFactory
{
    IShellContextScope CreateScope(ShellContext shell);
}
```

The scope is `IDisposable`. Always dispose it when you're done to release scoped resources.

---

## Iterating All Shells

The most common pattern is iterating over all shells and performing work for each one:

```csharp
using CShells;
using CShells.Hosting;
using Microsoft.Extensions.Hosting;

public class DataSyncWorker : BackgroundService
{
    private readonly IShellHost _shellHost;
    private readonly IShellContextScopeFactory _scopeFactory;
    private readonly ILogger<DataSyncWorker> _logger;

    public DataSyncWorker(
        IShellHost shellHost,
        IShellContextScopeFactory scopeFactory,
        ILogger<DataSyncWorker> logger)
    {
        _shellHost = shellHost;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var shell in _shellHost.AllShells)
            {
                using var scope = _scopeFactory.CreateScope(shell);

                var syncService = scope.ServiceProvider.GetService<IDataSyncService>();
                if (syncService is not null)
                    await syncService.SyncAsync(stoppingToken);
                else
                    _logger.LogDebug("Shell '{Shell}' does not have IDataSyncService", shell.Settings.Id.Value);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

Register the worker:

```csharp
builder.Services.AddHostedService<DataSyncWorker>();
```

---

## Working with a Specific Shell

If you need to target one specific shell:

```csharp
public class TenantReportGenerator
{
    private readonly IShellHost _shellHost;
    private readonly IShellContextScopeFactory _scopeFactory;

    public TenantReportGenerator(IShellHost shellHost, IShellContextScopeFactory scopeFactory)
    {
        _shellHost = shellHost;
        _scopeFactory = scopeFactory;
    }

    public async Task GenerateReportAsync(string tenantId, CancellationToken ct)
    {
        var shell = _shellHost.GetShell(new ShellId(tenantId));

        using var scope = _scopeFactory.CreateScope(shell);

        var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
        await reportService.GenerateAsync(ct);
    }
}
```

---

## Skipping Shells Without a Required Service

A shell may or may not have a particular feature enabled. Use `GetService<T>()` (nullable) instead of `GetRequiredService<T>()` when a service is optional:

```csharp
foreach (var shell in _shellHost.AllShells)
{
    using var scope = _scopeFactory.CreateScope(shell);

    var processor = scope.ServiceProvider.GetService<IQueueProcessor>();
    if (processor is null)
        continue;  // This shell doesn't have the queue processing feature

    await processor.ProcessPendingAsync(stoppingToken);
}
```

---

## Registering Background Workers as Shell Features

You can register a background worker from within a feature so it is automatically active for shells that enable the feature:

```csharp
[ShellFeature("Notifications", DependsOn = ["Core"])]
public class NotificationsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<INotificationSender, EmailNotificationSender>();
        // The worker is registered in the root DI container (not shell-scoped)
        // and uses IShellContextScopeFactory to access shell services
    }
}
```

Then register the worker once at the application level:

```csharp
builder.Services.AddHostedService<NotificationDispatchWorker>();
```

---

## Tips

- **Always dispose scopes** — `IShellContextScope` is `IDisposable`. Use `using` or `await using` as appropriate.
- **Use `GetService<T>()` for optional services** — not all shells will have all features enabled.
- **Re-query `IShellHost.AllShells` on each iteration** — the shell list can change at runtime when shells are added or removed.
- **Access `IShellHost` via injection, not closure** — always use the injected `IShellHost` reference; do not capture it in a static variable.
