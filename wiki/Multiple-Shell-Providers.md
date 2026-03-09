# Multiple Shell Providers

CShells supports registering multiple shell settings providers that are combined automatically. This allows you to load shells from different sources and compose them flexibly.

---

## How It Works

Providers are queried in the order they are registered. Each provider returns a list of `ShellSettings`. The results are merged by shell name: **the last provider to return a shell with a given name wins**.

```
Provider 1 (code-first)     Provider 2 (appsettings.json)     Provider 3 (database)
      │                              │                               │
      └──────────────────────────────┴───────────────────────────────┘
                                     │
                              Composite merge
                          (last provider wins by ID)
                                     │
                              ShellSettingsCache
```

---

## Registering Multiple Providers

```csharp
builder.AddShells(cshells =>
{
    // 1. Code-first defaults
    cshells.AddShell("Default", shell => shell
        .WithFeatures("Core", "Starter"));

    // 2. appsettings.json (overrides code-first for matching names)
    cshells.WithConfigurationProvider(builder.Configuration);

    // 3. Database (overrides anything above for matching names)
    cshells.WithProvider<DatabaseShellSettingsProvider>();
});
```

---

## Provider Registration Methods

### `AddShell` — Code-First

```csharp
cshells.AddShell("Tenant1", shell => shell
    .WithFeatures("Core", "Premium")
    .WithConfiguration("WebRouting:Path", "tenant1"));
```

Code-first shells are collected first and form the base layer.

### `WithConfigurationProvider` — `appsettings.json`

```csharp
cshells.WithConfigurationProvider(builder.Configuration);
// or with a custom section name:
cshells.WithConfigurationProvider(builder.Configuration, "TenantConfig");
```

### `WithProvider<T>` — Type-Registered Provider

The provider type is resolved from DI at startup, so it can receive constructor dependencies:

```csharp
cshells.WithProvider<DatabaseShellSettingsProvider>();
```

### `WithProvider(instance)` — Instance

```csharp
var provider = new InMemoryShellSettingsProvider(myShells);
cshells.WithProvider(provider);
```

### `WithProvider(factory)` — Factory Function

```csharp
cshells.WithProvider(sp =>
    new DatabaseShellSettingsProvider(sp.GetRequiredService<AppDbContext>()));
```

---

## Common Patterns

### Multi-Tenant SaaS

```csharp
builder.AddShells(cshells =>
{
    // Fallback defaults for new/unknown tenants
    cshells.AddShell("Default", shell => shell.WithFeatures("Core", "Starter"));

    // Active tenants from database
    cshells.WithProvider<ActiveTenantsProvider>();
});
```

### Environment-Based Overrides

```csharp
builder.AddShells(cshells =>
{
    // Base config from appsettings.json
    cshells.WithConfigurationProvider(builder.Configuration);

    if (builder.Environment.IsDevelopment())
    {
        // Add debug shells on top, or override existing ones
        cshells.AddShell("Debug", shell => shell.WithFeatures("Core", "Debug", "Swagger"));
    }
});
```

### Progressive Migration

```csharp
builder.AddShells(cshells =>
{
    // Legacy shells from old system
    cshells.WithProvider<LegacyShellProvider>();

    // Migrated shells from new system (override legacy ones by same ID)
    cshells.WithProvider<NewDatabaseShellProvider>();
});
```

---

## Built-in Provider Types

| Type | Description |
|---|---|
| `InMemoryShellSettingsProvider` | Immutable in-memory list (used internally for code-first shells) |
| `MutableInMemoryShellSettingsProvider` | Thread-safe, mutable in-memory list for dynamic scenarios |
| `ConfigurationShellSettingsProvider` | Reads from `IConfiguration` (typically `appsettings.json`) |
| `CompositeShellSettingsProvider` | Aggregates multiple providers (created automatically when >1 provider is registered) |
| `FluentStorageShellSettingsProvider` | Reads JSON files from disk or cloud storage |

### `MutableInMemoryShellSettingsProvider`

Use this when you need to programmatically add/remove shells outside of `IShellManager`:

```csharp
var mutableProvider = new MutableInMemoryShellSettingsProvider();

// Add or replace a shell
mutableProvider.AddOrUpdate(myShellSettings);

// Remove a shell
mutableProvider.Remove(new ShellId("Tenant1"));

// Register it as a provider
cshells.WithProvider(mutableProvider);
```

---

## Best Practices

- Register providers from **most general** (defaults) to **most specific** (overrides).
- Use `IShellManager.ReloadAllShellsAsync()` to refresh shells after external changes.
- Use `IShellManager.ReloadShellAsync(shellId)` to reload a single shell efficiently.
- Ensure custom providers are **thread-safe** — they may be called concurrently.
- Code-first shells and provider-based shells coexist naturally; no special opt-in is required.

---

## Targeted Provider Lookup

Providers implement `GetShellSettingsAsync(ShellId)` for efficient single-shell lookup during `ReloadShellAsync`. Built-in providers optimize this (e.g., `MutableInMemoryShellSettingsProvider` uses O(1) dictionary lookup). Custom providers can override this method; the default implementation enumerates all shells and filters by ID.

With a `CompositeShellSettingsProvider`, all providers are queried for the targeted shell ID, and the last non-null result wins — matching the same last-wins semantics used during full enumeration.

### Reload Notifications

During reload operations, `ShellReloading` and `ShellReloaded` notifications are emitted. Providers do not need to handle these notifications, but downstream consumers can observe them to react to configuration changes. See [Runtime Shell Management](Runtime-Shell-Management.md#reload-notification-ordering) for ordering details.
