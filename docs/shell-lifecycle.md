# Shell Lifecycle

CShells provides lifecycle hooks that let shell-scoped services perform work when a shell starts up or shuts down. This is the recommended approach for per-tenant initialization and cleanup — simpler than `IHostedService` because the work runs inside the shell's own DI scope automatically.

## Overview

| Interface | When It Runs | Use Cases |
|-----------|-------------|-----------|
| `IShellActivatedHandler` | After the shell's DI container is built and ready | Seed data, warm caches, start connections |
| `IShellDeactivatingHandler` | Before the shell's DI container is disposed | Flush buffers, close connections, release resources |

## IShellActivatedHandler

Invoked when a shell becomes active — either at application startup or when a shell is dynamically added via `IShellManager.AddShellAsync`.

```csharp
using CShells.Hosting;

public class SeedDataHandler(IPostRepository repo, ITenantInfo tenant) : IShellActivatedHandler
{
    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        // Runs once when this shell starts up.
        // The service provider is fully built, so all shell-scoped services are available.
        repo.Add("Welcome", $"Welcome to {tenant.TenantName}!", "System");
        return Task.CompletedTask;
    }
}
```

### Registration

Register the handler in your feature's `ConfigureServices`:

```csharp
[ShellFeature("Posts")]
public class PostsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPostRepository, InMemoryPostRepository>();
        services.AddSingleton<IShellActivatedHandler, SeedDataHandler>();
    }
}
```

### When It Fires

- **Application startup** — all configured shells are activated in order
- **Dynamic shell addition** — `await shellManager.AddShellAsync(settings)` activates the new shell
- Handlers run in **registration order**
- If a handler throws, the exception is logged and **propagated** (the shell activation fails)

## IShellDeactivatingHandler

Invoked before a shell is removed or during application shutdown. The shell's `IServiceProvider` is still alive, so all shell-scoped services are accessible during cleanup.

```csharp
using CShells.Hosting;

public class FlushAnalyticsHandler(IAnalyticsService analytics, ILogger<FlushAnalyticsHandler> logger)
    : IShellDeactivatingHandler
{
    public Task OnDeactivatingAsync(CancellationToken cancellationToken = default)
    {
        var counts = analytics.GetViewCounts();
        logger.LogInformation("Flushing {Count} analytics entries before shutdown", counts.Count);
        // Persist to external storage, etc.
        return Task.CompletedTask;
    }
}
```

### Registration

```csharp
[ShellFeature("Analytics")]
public class AnalyticsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAnalyticsService, InMemoryAnalyticsService>();
        services.AddSingleton<IShellDeactivatingHandler, FlushAnalyticsHandler>();
    }
}
```

### When It Fires

- **Application shutdown** — all shells are deactivated
- **Dynamic shell removal** — `await shellManager.RemoveShellAsync(shellId)`
- Handlers run in **reverse registration order** (LIFO)
- Exceptions are **logged but swallowed** — all handlers get a chance to run

## Lifecycle vs. Background Services

| Approach | Scope | Best For |
|----------|-------|----------|
| `IShellActivatedHandler` | Shell-scoped, runs once at activation | One-time setup: seed data, warm caches, validate config |
| `IShellDeactivatingHandler` | Shell-scoped, runs once at deactivation | Cleanup: flush buffers, close connections |
| `BackgroundService` + `IShellContextScopeFactory` | Host-scoped, runs continuously | Periodic work across all shells: heartbeats, polling, sync |

For one-time startup work per tenant, prefer `IShellActivatedHandler` — it's simpler and doesn't require manually creating shell scopes.

## Complete Example

A feature that warms a cache on activation and flushes it on deactivation:

```csharp
[ShellFeature("ProductCatalog")]
public class ProductCatalogFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IProductCache, InMemoryProductCache>();
        services.AddSingleton<IShellActivatedHandler, WarmCacheHandler>();
        services.AddSingleton<IShellDeactivatingHandler, FlushCacheHandler>();
    }
}

public class WarmCacheHandler(IProductCache cache, ILogger<WarmCacheHandler> logger)
    : IShellActivatedHandler
{
    public async Task OnActivatedAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Warming product cache...");
        await cache.LoadAsync(ct);
    }
}

public class FlushCacheHandler(IProductCache cache, ILogger<FlushCacheHandler> logger)
    : IShellDeactivatingHandler
{
    public async Task OnDeactivatingAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Flushing product cache...");
        await cache.FlushAsync(ct);
    }
}
```

