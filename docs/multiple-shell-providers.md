# Multiple Shell Providers

CShells supports loading shells from multiple sources simultaneously. Providers are queried in registration order, and later providers can override earlier ones (by shell ID).

## Basic Usage

### Configuration Provider Only (Default)

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithConfigurationProvider(builder.Configuration);
});
// Shells are loaded from appsettings.json via the configuration provider
```

### Feature Assembly Selection

Shell settings providers and feature assembly providers are configured independently.
Use `From*` members to select assembly sources directly, and `WithAssemblyProvider(...)` to attach provider-based sources.

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithConfigurationProvider(builder.Configuration);

    // Explicit feature assemblies
    shells.FromAssemblies(typeof(MyFeature).Assembly);

    // Append the built-in host-derived default explicitly when needed
    shells.FromHostAssemblies();

    // Append custom assembly providers additively
    shells.WithAssemblyProvider(sp =>
        new MyFeatureAssemblyProvider(sp.GetRequiredService<IModuleCatalog>()));
});
```

If you do not call any assembly-source method, CShells preserves the default host-derived discovery behavior. As soon as you call `FromAssemblies(...)`, `FromHostAssemblies()`, or `WithAssemblyProvider(...)`, discovery uses only the explicitly configured providers, concatenates their assembly contributions in registration order, and deduplicates assemblies before feature discovery.

### Code-First Shells

```csharp
builder.Services.AddCShells(shells =>
{
    shells.AddShell("Tenant1", shell =>
        shell.WithFeatures("Core", "Premium"));

    shells.AddShell("Tenant2", shell =>
        shell.WithFeatures("Core"));
});
```

### Combining Multiple Providers

```csharp
builder.Services.AddCShells(shells =>
{
    // 1. Code-first (loaded first)
    shells.AddShell("Default", shell =>
        shell.WithFeatures("Core"));

    // 2. Configuration (loaded second, can override code-first)
    shells.WithConfigurationProvider(builder.Configuration);

    // 3. Database (loaded third, can override both)
    shells.WithProvider<DatabaseShellSettingsProvider>();
});
```

## Provider Order and Overrides

Providers are queried in registration order. If multiple providers return a shell with the same ID, the **last one wins**.

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithConfigurationProvider(builder.Configuration);   // base
    shells.WithProvider<DatabaseShellSettingsProvider>();        // overrides

    if (builder.Environment.IsDevelopment())
    {
        shells.AddShell("Tenant1", shell =>
            shell.WithFeatures("Core", "Debug"));              // dev override
    }
});
```

## Built-in Providers

| Provider | Description |
|----------|-------------|
| `InMemoryShellSettingsProvider` | Immutable in-memory provider (code-first shells) |
| `MutableInMemoryShellSettingsProvider` | Thread-safe, mutable in-memory provider |
| `ConfigurationShellSettingsProvider` | Loads from `IConfiguration` (appsettings.json) |
| `FluentStorageShellSettingsProvider` | Loads JSON files from any FluentStorage blob store |
| `CompositeShellSettingsProvider` | Aggregates multiple providers (used internally) |

## FluentStorage Provider

Load shell settings from JSON files on disk, Azure Blob Storage, AWS S3, etc.:

```csharp
using CShells.Providers.FluentStorage;
using FluentStorage;

var blobStorage = StorageFactory.Blobs.DirectoryFiles("./Shells");

builder.Services.AddCShells(shells =>
{
    shells.WithFluentStorageProvider(blobStorage);
});
```

Each JSON file in the directory represents one shell:

```json
{
  "name": "Acme",
  "features": ["Core", "Posts", "Comments"],
  "configuration": {
    "WebRouting": { "Path": "acme" }
  }
}
```

## Custom Provider

Implement `IShellSettingsProvider`:

```csharp
public class DatabaseShellSettingsProvider(MyDbContext db) : IShellSettingsProvider
{
    public async Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(
        CancellationToken ct = default)
    {
        var tenants = await db.Tenants
            .Where(t => t.IsActive)
            .ToListAsync(ct);

        return tenants.Select(t => new ShellSettings(
            new ShellId(t.Id.ToString()),
            t.EnabledFeatures.ToList()));
    }

    public async Task<ShellSettings?> GetShellSettingsAsync(
        ShellId shellId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FindAsync([shellId.Name], ct);
        if (tenant is null || !tenant.IsActive) return null;

        return new ShellSettings(
            new ShellId(tenant.Id.ToString()),
            tenant.EnabledFeatures.ToList());
    }
}
```

The `GetShellSettingsAsync(ShellId)` overload enables targeted reload via `IShellManager.ReloadShellAsync`. Providers must implement both overloads; a simple approach is to enumerate all shells and filter by ID.

Register it:

```csharp
// Resolved from DI
shells.WithProvider<DatabaseShellSettingsProvider>();

// Via factory
shells.WithProvider(sp =>
    new DatabaseShellSettingsProvider(sp.GetRequiredService<MyDbContext>()));
```

## Runtime Shell Management

`IShellManager` supports adding, removing, updating, and reloading shells at runtime:

```csharp
public class TenantController(IShellManager shellManager)
{
    public async Task CreateTenantAsync(string id, List<string> features)
    {
        var settings = new ShellSettings(new ShellId(id), features);
        await shellManager.AddShellAsync(settings);
    }

    public async Task DeleteTenantAsync(string id) =>
        await shellManager.RemoveShellAsync(new ShellId(id));

    public async Task UpdateTenantAsync(ShellSettings settings) =>
        await shellManager.UpdateShellAsync(settings);

    public async Task ReloadTenantAsync(string id) =>
        await shellManager.ReloadShellAsync(new ShellId(id));

    public async Task ReloadAllAsync() =>
        await shellManager.ReloadAllShellsAsync();
}
```

### Reload Behavior with Multiple Providers

When using a `CompositeShellSettingsProvider` (the default when multiple providers are registered):

- **`ReloadShellAsync`** queries all providers for the targeted `ShellId` and keeps the last non-null result. If no provider defines the shell, an `InvalidOperationException` is thrown and the runtime state is left unchanged.
- **`ReloadAllShellsAsync`** queries all providers, reconciles the full set (last-wins), and invalidates all cached runtime contexts. Shells that no longer exist are removed; new shells are added; changed shells are updated.

During both operations, `ShellReloading` and `ShellReloaded` notifications are emitted to allow observers to react to reload lifecycle events.

## Best Practices

1. **Order matters** — register providers from most general to most specific
2. **Development overrides** — add dev-specific shells last
3. **Thread safety** — built-in providers are thread-safe; ensure custom ones are too
4. **Caching** — providers are called once at startup and again on `ReloadAllShellsAsync()`
