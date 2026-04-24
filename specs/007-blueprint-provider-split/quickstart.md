# Quickstart: Scale-Ready Blueprint Provider/Manager Split

**Feature**: [007-blueprint-provider-split](spec.md)
**Audience**: host developers integrating CShells 007+.

This is a fast tour of the new surface for people coming from feature `006`. It covers
the three most common host configurations — in-code shells, configuration-backed
shells, and database-backed shells — and shows how the admin-facing operations
(unregister, list, reload-active) look in practice.

## 1. Code-first shells (no catalogue source)

Identical to feature `006` at the call site. Nothing changes for host developers using
`AddShell`:

```csharp
builder.Services.AddCShells(cshells =>
{
    cshells.AddShell("payments", s => s
        .WithFeature<PaymentsFeature>()
        .WithConfiguration("stripe:key", configuration["Stripe:Key"]));

    cshells.AddShell("billing", s => s
        .WithFeature<BillingFeature>());
});
```

Under the hood: every `AddShell(...)` call registers a `DelegateShellBlueprint` with
the singleton `InMemoryShellBlueprintProvider`. The provider exposes those blueprints
through the new lookup/listing contract without you noticing.

## 2. Configuration-backed shells

```csharp
builder.Services.AddCShells(cshells =>
{
    cshells.AddShellsFromConfiguration(configuration.GetSection("Shells"));
});
```

With an `appsettings.json` of the form:

```json
{
  "Shells": {
    "tenant-acme": {
      "Features": ["Billing", "Notifications"]
    },
    "tenant-widgets": {
      "Features": ["Billing"]
    }
  }
}
```

The configuration provider is read-only (no manager). Attempting to
`UnregisterBlueprintAsync("tenant-acme")` raises `BlueprintNotMutableException`.

## 3. Database-backed shells (mutable)

```csharp
builder.Services.AddCShells(cshells =>
{
    // Baseline shells in code (read-only)
    cshells.AddShell("platform", s => s.WithFeature<PlatformFeature>());

    // Tenant shells from blob storage (read/write)
    cshells.AddFluentStorageBlueprints(opts =>
    {
        opts.Container = "tenant-shells";
        opts.ConnectionString = configuration["Blob:ConnectionString"];
    });
});
```

The FluentStorage provider implements *both* `IShellBlueprintProvider` and
`IShellBlueprintManager`. Tenants live in blobs; operators mutate them via the manager.
Baseline `platform` shell stays immutable.

## 4. Lazy activation

With any of the above configurations, no shell is built at host startup. On first
request for a shell name, the middleware calls `GetOrActivateAsync` which:

1. Fast-path check: is there an active generation for this name? Yes → return it.
2. Acquire the name's per-slot semaphore.
3. Ask the composite provider for the blueprint (lookup is O(1) or O(log N)).
4. Build the shell, run initializers, promote to Active, release the semaphore.

Concurrent requests for the same inactive name are serialized on the semaphore; the
lookup and build happen exactly once regardless of request count.

## 5. Pre-warming specific shells at startup

If a particular shell must be hot from `t=0`:

```csharp
builder.Services.AddCShells(cshells =>
{
    cshells.AddShell("platform", s => s.WithFeature<PlatformFeature>());
    cshells.PreWarmShells("platform");  // activate at StartAsync
});
```

Pre-warming is opt-in and per-name. Omit it entirely and startup is O(1) in catalogue
size.

## 6. Explicit activation from host code

```csharp
var registry = app.Services.GetRequiredService<IShellRegistry>();
var shell = await registry.GetOrActivateAsync("tenant-acme");
using var scope = shell.BeginScope();
var service = scope.ServiceProvider.GetRequiredService<IBillingService>();
// ...
```

`GetOrActivateAsync` replaces the feature-`006` pattern of
`registry.GetActive(name) ?? await registry.ActivateAsync(name)`. The latter is still
available but only for hosts that want to force-activate with an error if the shell is
already live.

## 7. Listing the catalogue

```csharp
var query = new ShellListQuery(Cursor: null, Limit: 100, NamePrefix: "tenant-");
var page = await registry.ListAsync(query);

foreach (var entry in page.Items)
{
    Console.WriteLine(
        $"{entry.Name} [{entry.SourceId}] {(entry.Mutable ? "R/W" : "R/O")} " +
        $"Active={entry.State?.ToString() ?? "<inactive>"}");
}

while (page.NextCursor is not null)
{
    query = query with { Cursor = page.NextCursor };
    page = await registry.ListAsync(query);
    // ...
}
```

The left join between catalogue (provider) and lifecycle (registry) is transparent —
inactive blueprints appear with null `State` / `ActiveGeneration`.

## 8. Removing a shell

```csharp
try
{
    await registry.UnregisterBlueprintAsync("tenant-acme");
}
catch (BlueprintNotMutableException ex)
{
    // Source is read-only (e.g., appsettings.json). Operator has to edit
    // the source and restart, or use a different name.
    logger.LogWarning(ex, "Cannot unregister '{Name}'; source is read-only.", ex.Name);
}
```

The unregister runs two ordered phases:

1. Invoke the owning manager's `DeleteAsync` — persistent state gone first.
2. Drain the active generation if any, dispose it, remove the in-memory slot.

If phase 1 fails, phase 2 never runs. If phase 2 fails (e.g., drain timeout with force
misconfigured), the persistent state is already gone and the in-memory slot is
force-cleared so the registry and catalogue stay consistent.

## 9. Reloading every active shell

```csharp
var results = await registry.ReloadActiveAsync(new ReloadOptions(MaxDegreeOfParallelism: 4));
foreach (var r in results)
{
    logger.LogInformation(
        "Reloaded '{Name}' gen {Gen} → {Status}",
        r.Descriptor.Name, r.Descriptor.Generation, r.Status);
}
```

Only currently-active shells are touched. Inactive blueprints are left inactive —
consistent with feature `007`'s lazy model. A failure in one shell's reload does not
abort the batch.

## 10. Implementing your own provider

```csharp
public sealed class SqlShellBlueprintProvider :
    IShellBlueprintProvider, IShellBlueprintManager
{
    private readonly SqlConnection _db;

    public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken ct)
    {
        var blueprint = LoadFromDbByName(name);
        return Task.FromResult(blueprint is null
            ? null
            : new ProvidedBlueprint(blueprint, Manager: this));
    }

    public Task<BlueprintPage> ListAsync(BlueprintListQuery q, CancellationToken ct)
    {
        // Page over SQL using (name > cursor) WHERE name LIKE '{prefix}%'
        // ORDER BY name. Return at most q.Limit rows; encode last name as
        // NextCursor or null when exhausted.
    }

    public bool Owns(string name) => LooksLikeTenantName(name);

    public Task CreateAsync(ShellSettings settings, CancellationToken ct) =>
        InsertRowAsync(settings);

    public Task UpdateAsync(ShellSettings settings, CancellationToken ct) =>
        UpdateRowAsync(settings);

    public Task DeleteAsync(string name, CancellationToken ct) =>
        DeleteRowAsync(name);
}
```

Register with DI:

```csharp
services.AddSingleton<SqlShellBlueprintProvider>();
services.AddSingleton<IShellBlueprintProvider>(sp => sp.GetRequiredService<SqlShellBlueprintProvider>());
services.AddSingleton<IShellBlueprintManager>(sp => sp.GetRequiredService<SqlShellBlueprintProvider>());
```

Ordering in DI determines the composite's lookup precedence — earlier-registered
providers are probed first.

## 11. Migration from feature 006

| Feature 006 code | Feature 007 replacement |
|-------------------|-------------------------|
| `registry.RegisterBlueprint(bp)` | `cshells.AddShell(name, configure)` at composition time, or custom `IShellBlueprintProvider` |
| `registry.GetBlueprint(name)` | `await registry.GetBlueprintAsync(name)` |
| `registry.GetBlueprintNames()` | `await registry.ListAsync(new ShellListQuery(Limit: 500))` (paged) |
| `await registry.ReloadAllAsync()` | `await registry.ReloadActiveAsync()` |
| `registry.GetActive(name) ?? await registry.ActivateAsync(name)` | `await registry.GetOrActivateAsync(name)` |
| Eager `IShellBlueprintProvider` with `GetBlueprintsAsync()` | Lazy `IShellBlueprintProvider` with `GetAsync` / `ListAsync` |

No compatibility shims. The migration touches composition root + any host code calling
the removed registry members.
