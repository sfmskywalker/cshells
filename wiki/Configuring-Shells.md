# Configuring Shells

A shell is configured with a name, a list of enabled features, and an optional configuration section. CShells supports multiple ways to provide this configuration.

---

## Shell Settings Structure

Each shell has:

| Property | Description |
|---|---|
| `Name` | Unique shell identifier (e.g., `"Default"`, `"Acme"`) |
| `Features` | List of enabled feature names (or objects with inline settings) |
| `Configuration` | Shell-specific key/value configuration (hierarchical) |

---

## Option A: `appsettings.json`

The default configuration source. CShells reads from the `"CShells"` section by default.

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": ["Core", "Weather"],
        "Configuration": {
          "WebRouting": {
            "Path": ""
          }
        }
      },
      {
        "Name": "Admin",
        "Features": [
          "Core",
          { "Name": "Admin", "MaxUsers": 100, "EnableAuditLog": true }
        ],
        "Configuration": {
          "WebRouting": {
            "Path": "admin",
            "RoutePrefix": "api/v1"
          }
        }
      }
    ]
  }
}
```

Register with the default section name:

```csharp
builder.AddShells();  // reads "CShells" section
```

Or specify a custom section:

```csharp
builder.AddShells("MyCustomSection");
```

---

## Option B: Code-First Configuration

Define shells directly in `Program.cs` using the fluent `AddShell` API:

```csharp
builder.AddShells(cshells =>
{
    cshells.AddShell("Default", shell => shell
        .WithFeatures("Core", "Weather")
        .WithConfiguration("WebRouting:Path", ""));

    cshells.AddShell("Admin", shell => shell
        .WithFeature("Core")
        .WithFeature("Admin", settings => settings
            .WithSetting("MaxUsers", 100)
            .WithSetting("EnableAuditLog", true))
        .WithConfiguration("WebRouting:Path", "admin")
        .WithConfiguration("WebRouting:RoutePrefix", "api/v1"));
});
```

You can also use type-safe feature references:

```csharp
cshells.AddShell("Default", shell => shell
    .WithFeature<CoreFeature>()
    .WithFeature<WeatherFeature>(f =>
    {
        // Set properties directly on the feature instance before ConfigureServices runs
        f.ApiKey = "my-key";
        f.TimeoutSeconds = 30;
    }));
```

The `Action<TFeature>` configurator runs after configuration binding but before `ConfigureServices`, so code always wins over `appsettings.json` values.

---

## Option C: FluentStorage (External JSON Files)

Load shells from individual JSON files. Useful for separating per-tenant configuration from the main config file and for cloud storage scenarios.

**Install:**

```bash
dotnet add package CShells.Providers.FluentStorage
dotnet add package FluentStorage
```

**Create shell JSON files** (e.g., `Shells/Default.json`):

```json
{
  "Name": "Default",
  "Features": ["Core", "Weather"],
  "Configuration": {
    "WebRouting": {
      "Path": ""
    }
  }
}
```

**Register the provider:**

```csharp
using FluentStorage;
using CShells.Providers.FluentStorage;

var shellsPath = Path.Combine(builder.Environment.ContentRootPath, "Shells");
var blobStorage = StorageFactory.Blobs.DirectoryFiles(shellsPath);

builder.AddShells(cshells =>
{
    cshells.WithFluentStorageProvider(blobStorage);
});
```

The FluentStorage provider supports Azure Blob Storage, AWS S3, and other backends in addition to local disk.

---

## Option D: Custom Provider

Implement `IShellSettingsProvider` to load shells from any source (database, API, etc.):

```csharp
public class DatabaseShellSettingsProvider : IShellSettingsProvider
{
    private readonly AppDbContext _dbContext;

    public DatabaseShellSettingsProvider(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var tenants = await _dbContext.Tenants
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);

        return tenants.Select(t => new ShellSettings(
            new ShellId(t.Id.ToString()),
            t.EnabledFeatures));
    }
}
```

Register it:

```csharp
builder.AddShells(cshells =>
{
    cshells.WithProvider<DatabaseShellSettingsProvider>();
});
```

---

## Multiple Providers

You can register multiple providers. They are queried in registration order; if two providers return a shell with the same name, **the last one wins**.

```csharp
builder.AddShells(cshells =>
{
    // 1. Code-first defaults
    cshells.AddShell("Default", shell => shell.WithFeatures("Core"));

    // 2. Configuration from appsettings.json (may override code-first)
    cshells.WithConfigurationProvider(builder.Configuration);

    // 3. Database overrides (overrides anything above for matching shell names)
    cshells.WithProvider<DatabaseShellSettingsProvider>();
});
```

This pattern is useful for:

- **Multi-environment**: Base config in `appsettings.json`, environment overrides from a database or environment variables.
- **Progressive migration**: Legacy shells from one provider, migrated shells from another.
- **Development overrides**: Production config with debug shells layered on top in development.

See [Multiple Providers](Multiple-Shell-Providers) for detailed patterns and examples.

---

## WebRouting Configuration

The `WebRouting` configuration section controls how a shell is matched to incoming requests and how its endpoints are prefixed.

| Key | Description | Example |
|---|---|---|
| `WebRouting:Path` | URL path prefix for shell resolution (empty string = root) | `"tenants/acme"` |
| `WebRouting:Host` | Hostname for host-based resolution | `"acme.example.com"` |
| `WebRouting:RoutePrefix` | Additional prefix applied to all shell endpoints | `"api/v1"` |

Example: with `Path = "acme"` and `RoutePrefix = "api/v1"`, an endpoint mapped at `"products"` is accessible at `/acme/api/v1/products`.

---

## Built-in Providers Reference

| Provider | Class | Use Case |
|---|---|---|
| Configuration | `ConfigurationShellSettingsProvider` | `appsettings.json` and any `IConfiguration` source |
| In-memory (immutable) | `InMemoryShellSettingsProvider` | Code-first, testing |
| In-memory (mutable) | `MutableInMemoryShellSettingsProvider` | Dynamic runtime scenarios |
| Composite | `CompositeShellSettingsProvider` | Aggregates multiple providers (used automatically) |
| FluentStorage | `FluentStorageShellSettingsProvider` | Files on disk or cloud storage |
