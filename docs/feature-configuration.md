# Feature Configuration

CShells provides a convention-based configuration system that lets features receive per-shell settings from `appsettings.json`, environment variables, or any `IConfiguration` source.

## Overview

Features can be configured in four ways:

1. **Object-map configuration** — features as property keys with `true`, `false`, or settings objects (recommended)
2. **Inline configuration** — settings defined directly alongside the feature name in the `Features` array
3. **Explicit configuration** — implement `IConfigurableFeature<TOptions>` for strongly-typed options
4. **Manual configuration** — use `IConfiguration` or `IOptions<T>` directly in `ConfigureServices`

## Object-Map Configuration (Recommended)

The `Features` property supports an object-map syntax where each property key is the feature name and the property value declares whether the feature is enabled, disabled, or configured:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Core": true,
          "LegacyAuth": false,
          "Analytics": { "TopPostsCount": 10 }
        }
      }
    }
  }
}
```

Use `true` to enable a feature with default settings, `false` to disable a feature, and an object to enable a feature with direct settings. Empty object `{}` remains valid for existing configuration and also enables the feature with defaults. All properties within a feature's object are treated as settings — there is no special `Name` or `Enabled` control property since the object key serves as the feature name.

This form is friendly to layered configuration. A Docker-mounted file or environment variable can disable a feature supplied by application defaults:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Identity": false
        }
      }
    }
  }
}
```

String values `"true"` and `"false"` are also accepted case-insensitively so string-valued providers can enable or disable features:

```bash
CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY=false
```

Do not repeat the feature name inside an object-map entry. If a `Name` property is present there, it is delivered to the feature as configuration and does not rename, enable, disable, or replace the feature identified by the object key.

### Validation Rules

- **No duplicates**: Each feature name must appear exactly once per shell.
- **No mixing**: A shell's `Features` value must be entirely array syntax or entirely object-map syntax. Mixing both styles is rejected with an error that identifies the affected shell.
- **Values must be boolean or object**: In object-map syntax, every feature value must be `true`, `false`, string `"true"` / `"false"`, or a JSON object. Values such as `"yes"`, `0`, `null`, and arrays are rejected.
- **Unknown features**: Unknown feature names set to `false` are ignored as no-op disablements; unknown feature names set to `true` or to an object are rejected before activation.
- **No silent skips**: Blank array entries and array objects with missing or blank `Name` values are rejected before the shell is activated.

## Legacy Inline Configuration (Array Syntax)

Object-map syntax is preferred because it merges predictably across configuration layers. Legacy `Features` arrays are still normalized when used directly. Each array entry can be either a string (feature name) or an object with `Name` plus any additional properties:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": [
          "Core",
          {
            "Name": "Analytics",
            "TopPostsCount": 10
          }
        ]
      }
    }
  }
}
```

For compatibility with older configuration, array objects may also place settings under a `Settings` wrapper:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": [
          {
            "Name": "Analytics",
            "Settings": { "TopPostsCount": 10 }
          }
        ]
      }
    }
  }
}
```

Do not mix the `Settings` wrapper with direct sibling settings in the same feature entry.

### Consuming Inline Settings

Bind the settings from the shell's `IConfiguration` in `ConfigureServices`:

```csharp
[ShellFeature("Analytics")]
public class AnalyticsFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<AnalyticsOptions>()
            .Configure<IConfiguration>((opts, config) =>
                config.GetSection("Analytics").Bind(opts));

        services.AddSingleton<IAnalyticsService, InMemoryAnalyticsService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? env)
    {
        endpoints.MapGet("/analytics", (
            IAnalyticsService svc,
            IOptions<AnalyticsOptions> opts) =>
        {
            var top = svc.GetViewCounts()
                .OrderByDescending(kv => kv.Value)
                .Take(opts.Value.TopPostsCount);
            return Results.Ok(top);
        });
    }
}

public class AnalyticsOptions
{
    public int TopPostsCount { get; set; } = 5;
}
```

## Explicit Configuration with IConfigurableFeature&lt;T&gt;

For more complex scenarios, implement `IConfigurableFeature<TOptions>`:

```csharp
[ShellFeature("Database")]
public class DatabaseFeature : IShellFeature, IConfigurableFeature<DatabaseOptions>
{
    private DatabaseOptions _options = new();

    // Called automatically after the options are bound from configuration
    public void Configure(DatabaseOptions options) => _options = options;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlServer(_options.ConnectionString));
    }
}

public class DatabaseOptions
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeout { get; set; } = 30;
}
```

### Configuration

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Core": true,
          "Database": { "ConnectionString": "Server=localhost;Database=App;..." }
        }
      }
    }
  }
}
```

Or use the shell's `Configuration` section:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Core": true,
          "Database": true
        },
        "Configuration": {
          "Database": {
            "ConnectionString": "Server=localhost;Database=App;..."
          }
        }
      }
    }
  }
}
```

## Configuration Precedence

Settings are resolved in this order (highest wins):

1. **Environment variables** — `CSHELLS__SHELLS__DEFAULT__FEATURES__FeatureName__Property`
2. **Feature object-map configuration** — settings in the `Features` map
3. **Shell Configuration section** — `CShells:Shells.<ShellName>.Configuration.FeatureName`
4. **Root configuration** — `FeatureName:Property`
5. **Property defaults** — default values on the options class

## Configuration Validation

### DataAnnotations

```csharp
public class DatabaseOptions
{
    [Required]
    public string ConnectionString { get; set; } = "";

    [Range(10, 300)]
    public int CommandTimeout { get; set; } = 30;
}
```

### Custom Validation

Implement `IFeatureConfigurationValidator`:

```csharp
public class SecurityValidator : IFeatureConfigurationValidator
{
    public void Validate(object target, string contextName)
    {
        if (target is DatabaseOptions opts &&
            opts.ConnectionString.Contains("password=", StringComparison.OrdinalIgnoreCase))
        {
            throw new FeatureConfigurationValidationException(
                contextName,
                ["Connection string should not contain plain-text passwords"]);
        }
    }
}
```

## Environment Variables

Override any setting with environment variables using hierarchical naming:

```bash
# Override a feature setting for a specific shell
CSHELLS__SHELLS__DEFAULT__CONFIGURATION__DATABASE__CONNECTIONSTRING="Server=prod;..."

# Disable a default feature for a specific shell
CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY=false
```

## Secrets Management

Never store secrets in `appsettings.json`. Use the standard .NET mechanisms:

```bash
# Development — User Secrets
dotnet user-secrets set "CShells:Shells:Default:Configuration:Database:ConnectionString" "Server=dev;..."

# Production — Environment Variables or Azure Key Vault
```

CShells works with any `IConfiguration` provider, including Azure Key Vault, AWS Secrets Manager, and HashiCorp Vault.

## Best Practices

1. **Use object-map configuration** for simple, per-shell feature enablement and settings
2. **Use `IConfigurableFeature<T>`** when you need typed access at feature construction time
3. **Provide sensible defaults** so features work out of the box
4. **Validate at startup** using DataAnnotations or custom validators
5. **Never commit secrets** — use environment variables or secret managers
