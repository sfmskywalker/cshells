# Feature Configuration

CShells provides a convention-based configuration system that binds settings from `appsettings.json`, environment variables, or any `IConfiguration` source directly to feature instances.

---

## Configuration Sources

Shell features can receive configuration from three places, listed in order of precedence (highest first):

1. **Environment variables** — override everything
2. **Inline feature settings** — settings placed alongside the feature name in the `Features` section
3. **Shell `Configuration` section** — `CShells:Shells[n]:Configuration:<FeatureName>`

---

## Object-Map Feature Settings (Recommended)

Use an object-map where each property key is the feature name and the value supplies per-feature settings:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": {
          "Core": {},
          "Database": {
            "ConnectionString": "Server=prod;Database=App;Integrated Security=true",
            "CommandTimeout": 60
          },
          "Logging": {}
        }
      }
    ]
  }
}
```

Features with no settings use an empty object `{}`. All properties within a feature's value are treated as settings — the property key is the feature name.

### Validation Rules

- **No duplicates**: Each feature name must appear exactly once per shell.
- **No mixing**: A shell must use either array syntax or object-map syntax exclusively. Mixing both is rejected.
- **Values must be objects**: In object-map syntax, each feature value must be a JSON object.

---

## Array Feature Settings

Place settings in a mixed array of strings and objects (the original syntax, still fully supported):

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": [
          "Core",
          {
            "Name": "Database",
            "ConnectionString": "Server=prod;Database=App;Integrated Security=true",
            "CommandTimeout": 60
          },
          "Logging"
        ]
      }
    ]
  }
}
```

This is the recommended approach when settings are specific to one feature in one shell.

---

## Shell `Configuration` Section

Use the `Configuration` section for shell-level configuration that applies to multiple features or that you want separated from the feature list:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": ["Core", "Database", "Logging"],
        "Configuration": {
          "Database": {
            "ConnectionString": "Server=prod;Database=App;Integrated Security=true",
            "CommandTimeout": 60
          },
          "WebRouting": {
            "Path": "",
            "RoutePrefix": "api/v1"
          }
        }
      }
    ]
  }
}
```

---

## `IConfigurableFeature<T>` — Strongly-Typed Binding

Implement `IConfigurableFeature<TOptions>` to receive an options instance automatically bound from configuration. The `Configure` method is called after binding and before `ConfigureServices`.

```csharp
public class DatabaseOptions
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeout { get; set; } = 30;
    public bool EnableRetryOnFailure { get; set; } = true;
}

[ShellFeature("Database")]
public class DatabaseFeature : IShellFeature, IConfigurableFeature<DatabaseOptions>
{
    private DatabaseOptions _options = new();

    public void Configure(DatabaseOptions options)
    {
        _options = options;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(opts =>
        {
            opts.UseSqlServer(_options.ConnectionString,
                sql => { if (_options.EnableRetryOnFailure) sql.EnableRetryOnFailure(); });
        });
    }
}
```

The configuration section used for binding is determined by the feature name (from `[ShellFeature]` attribute or class name).

### Multiple Options Classes

A feature can implement `IConfigurableFeature<T>` multiple times:

```csharp
[ShellFeature("Messaging")]
public class MessagingFeature :
    IShellFeature,
    IConfigurableFeature<MessagingOptions>,
    IConfigurableFeature<CacheOptions>
{
    private MessagingOptions _messaging = new();
    private CacheOptions _cache = new();

    public void Configure(MessagingOptions options) => _messaging = options;
    public void Configure(CacheOptions options) => _cache = options;

    public void ConfigureServices(IServiceCollection services)
    {
        // Use both option sets
    }
}
```

---

## Manual Configuration via `IConfiguration`

Access the shell's `IConfiguration` directly inside `ConfigureServices` for maximum flexibility:

```csharp
public class WeatherFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWeatherService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var apiKey = config["Weather:ApiKey"];
            return new WeatherService(apiKey);
        });
    }
}
```

The `IConfiguration` resolved from the shell's `IServiceProvider` is built from the shell's own `Configuration` section.

---

## Accessing Shell Configuration via Constructor

Inject `ShellSettings` in the feature constructor to access `ConfigurationData` directly:

```csharp
public class TenantFeature(ShellSettings settings) : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Access raw configuration data stored as key:value pairs
        if (settings.ConfigurationData.TryGetValue("WebRouting:Path", out var path))
        {
            // use path
        }
    }
}
```

---

## Configuration Validation

### DataAnnotations (Built-in)

Decorate options properties with standard `System.ComponentModel.DataAnnotations` attributes:

```csharp
using System.ComponentModel.DataAnnotations;

public class DatabaseOptions
{
    [Required(ErrorMessage = "ConnectionString is required")]
    [MinLength(10)]
    public string ConnectionString { get; set; } = "";

    [Range(10, 300, ErrorMessage = "CommandTimeout must be between 10 and 300 seconds")]
    public int CommandTimeout { get; set; } = 30;
}
```

DataAnnotations validation runs automatically when the options are bound. The application fails fast at startup if validation fails.

---

## Secrets Management

Never store secrets in `appsettings.json`. Use environment variables or a secrets manager.

### Development (User Secrets)

```bash
dotnet user-secrets set "CShells:Shells:0:Configuration:Database:ConnectionString" "Server=localhost;..."
```

### Production (Environment Variables)

Use double-underscore (`__`) as the hierarchy separator:

```bash
CShells__Shells__0__Configuration__Database__ConnectionString="Server=prod;..."
```

### Azure Key Vault / AWS Secrets Manager

CShells works with any standard `IConfiguration` provider:

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://myvault.vault.azure.net/"),
    new DefaultAzureCredential());
```

Key Vault secret names use `--` as the hierarchy separator:

```
CShells--Shells--0--Configuration--Database--ConnectionString
```

---

## Best Practices

- Use `IConfigurableFeature<T>` for complex, multi-property configuration.
- Use `[Required]` and `[Range]` to fail fast at startup when required config is missing.
- Provide sensible defaults so features work with minimal configuration.
- Never store connection strings or API keys in source-controlled config files.
