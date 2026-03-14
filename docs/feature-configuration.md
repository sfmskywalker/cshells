# Feature Configuration

CShells provides a convention-based configuration system that lets features receive per-shell settings from `appsettings.json`, environment variables, or any `IConfiguration` source.

## Overview

Features can be configured in four ways:

1. **Object-map configuration** — features as property keys with settings as values (recommended)
2. **Inline configuration** — settings defined directly alongside the feature name in the `Features` array
3. **Explicit configuration** — implement `IConfigurableFeature<TOptions>` for strongly-typed options
4. **Manual configuration** — use `IConfiguration` or `IOptions<T>` directly in `ConfigureServices`

## Object-Map Configuration (Recommended)

The `Features` property supports an object-map syntax where each property key is the feature name and the property value provides the feature settings:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": {
          "Core": {},
          "Analytics": { "TopPostsCount": 10 }
        }
      }
    ]
  }
}
```

Features with no settings use an empty object `{}`. All properties within a feature's object are treated as settings — there is no special `Name` property since the object key serves as the feature name.

### Validation Rules

- **No duplicates**: Each feature name must appear exactly once per shell.
- **No mixing**: A shell's `Features` value must be entirely array syntax or entirely object-map syntax. Mixing both styles is rejected with an error that identifies the affected shell.
- **Values must be objects**: In object-map syntax, every feature value must be a JSON object (not a string, number, or array).

## Inline Configuration (Array Syntax)

Each entry in the `Features` array can be either a string (feature name) or an object with `Name` plus any additional properties:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": [
          "Core",
          {
            "Name": "Analytics",
            "TopPostsCount": 10
          }
        ]
      }
    ]
  }
}
```

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
    "Shells": [
      {
        "Name": "Default",
        "Features": [
          "Core",
          { "Name": "Database", "ConnectionString": "Server=localhost;Database=App;..." }
        ]
      }
    ]
  }
}
```

Or use the shell's `Configuration` section:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": ["Core", "Database"],
        "Configuration": {
          "Database": {
            "ConnectionString": "Server=localhost;Database=App;..."
          }
        }
      }
    ]
  }
}
```

## Configuration Precedence

Settings are resolved in this order (highest wins):

1. **Environment variables** — `Shells__Default__Configuration__FeatureName__Property`
2. **Inline feature configuration** — settings in the `Features` array
3. **Shell Configuration section** — `CShells:Shells[].Configuration.FeatureName`
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
Shells__Default__Configuration__Database__ConnectionString="Server=prod;..."
```

## Secrets Management

Never store secrets in `appsettings.json`. Use the standard .NET mechanisms:

```bash
# Development — User Secrets
dotnet user-secrets set "CShells:Shells:0:Configuration:Database:ConnectionString" "Server=dev;..."

# Production — Environment Variables or Azure Key Vault
```

CShells works with any `IConfiguration` provider, including Azure Key Vault, AWS Secrets Manager, and HashiCorp Vault.

## Best Practices

1. **Use inline configuration** for simple, per-shell settings
2. **Use `IConfigurableFeature<T>`** when you need typed access at feature construction time
3. **Provide sensible defaults** so features work out of the box
4. **Validate at startup** using DataAnnotations or custom validators
5. **Never commit secrets** — use environment variables or secret managers
