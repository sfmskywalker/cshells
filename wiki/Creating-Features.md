# Creating Features

Features are the building blocks of CShells. A feature is a class that implements one of the feature interfaces and (optionally) carries a `[ShellFeature]` attribute.

---

## Feature Interfaces

| Interface | Package | Purpose |
|---|---|---|
| `IShellFeature` | `CShells.Abstractions` | Register services into the shell's DI container |
| `IWebShellFeature` | `CShells.AspNetCore.Abstractions` | Register services **and** HTTP endpoints |
| `IMiddlewareShellFeature` | `CShells.AspNetCore.Abstractions` | Register services **and** ASP.NET Core middleware |
| `IConfigurableFeature<T>` | `CShells.Abstractions` | Receive strongly-typed options bound from configuration |
| `IPostConfigureShellServices` | `CShells.Abstractions` | Run code after **all** features have configured services |

---

## `IShellFeature` — Service Registration

The simplest feature type. Implement `ConfigureServices` to add services to the shell's DI container.

```csharp
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

public class CoreFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITimeService, TimeService>();
        services.AddScoped<IAuditService, AuditService>();
    }
}
```

Features are instantiated via `ActivatorUtilities.CreateInstance` using the **application root** `IServiceProvider`. Their constructors can receive:

- Any root-level service (logging, configuration, `IOptions<T>`)
- `ShellSettings` — the settings for the shell being built
- `ShellFeatureContext` — settings plus all discovered feature descriptors

```csharp
// Inject ShellSettings for simple access to shell config
public class DatabaseFeature(ShellSettings settings) : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // settings.ConfigurationData is populated from the shell's Configuration section
        services.AddDbContext<AppDbContext>();
    }
}
```

> **Important:** Feature constructors must **not** depend on services registered by other features in their `ConfigureServices` methods. Those services only exist after the shell's `IServiceProvider` is built.

---

## `IWebShellFeature` — Services + HTTP Endpoints

Extends `IShellFeature` with `MapEndpoints`, which is called once per shell to register HTTP endpoints.

```csharp
using CShells.AspNetCore.Features;
using Microsoft.Extensions.DependencyInjection;

[ShellFeature("Payment", DisplayName = "Payment API", DependsOn = ["Core"])]
public class PaymentFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPaymentProcessor, StripePaymentProcessor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapPost("payment/process", async (PaymentRequest req, IPaymentProcessor processor) =>
            await processor.ProcessAsync(req));

        endpoints.MapGet("payment/status/{id}", (string id, IPaymentProcessor processor) =>
            processor.GetStatus(id));
    }
}
```

Endpoints registered here are automatically prefixed with the shell's `WebRouting:Path` and `WebRouting:RoutePrefix`.

---

## `IMiddlewareShellFeature` — Services + Middleware

Extends `IShellFeature` with `UseMiddleware`, which registers ASP.NET Core middleware scoped to the shell's path prefix.

```csharp
using CShells.AspNetCore.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

public class RequestLoggingFeature : IMiddlewareShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IRequestLogger, RequestLogger>();
    }

    public void UseMiddleware(IApplicationBuilder app, IHostEnvironment? environment)
    {
        app.Use(async (context, next) =>
        {
            var logger = context.RequestServices.GetRequiredService<IRequestLogger>();
            logger.LogRequest(context);
            await next();
        });
    }
}
```

Middleware registered here runs inside the shell's service provider scope, so services resolved from `HttpContext.RequestServices` come from the correct shell.

---

## `[ShellFeature]` Attribute

The attribute is **optional**. Use it when you need to:

- Set an explicit feature name (otherwise the class name is used)
- Set a display name
- Declare dependencies on other features
- Add metadata

```csharp
// No attribute — feature name is "WeatherFeature"
public class WeatherFeature : IShellFeature { ... }

// With explicit name
[ShellFeature("Weather")]
public class WeatherFeature : IShellFeature { ... }

// With display name and string-based dependencies
[ShellFeature("Weather", DisplayName = "Weather API", DependsOn = ["Core"])]
public class WeatherFeature : IShellFeature { ... }

// With strongly-typed dependencies (name resolved from CoreFeature's attribute or class name)
[ShellFeature("Weather", DependsOn = [typeof(CoreFeature)])]
public class WeatherFeature : IShellFeature { ... }

// Mixed string and type dependencies
[ShellFeature("Weather", DependsOn = [typeof(CoreFeature), "Logging"])]
public class WeatherFeature : IShellFeature { ... }
```

When `typeof(CoreFeature)` is used in `DependsOn`, the feature name is resolved from `CoreFeature`'s `[ShellFeature]` attribute (or its class name if no attribute is present). Renaming the attribute automatically updates all dependents.

---

## Feature Dependencies

Declare dependencies with `DependsOn` so that CShells calls `ConfigureServices` in the correct topological order.

```csharp
[ShellFeature("Reporting", DependsOn = [typeof(CoreFeature), typeof(DatabaseFeature)])]
public class ReportingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IReportingService, ReportingService>();
    }
}
```

CShells automatically:
- Includes all transitive dependencies
- Sorts features in dependency order
- Detects cycles and invalid dependencies at startup

### Inferring Dependencies

If a feature is a specialization of another, implement `IInfersDependenciesFrom<TBaseFeature>` to automatically inherit the base feature's dependencies:

```csharp
public class SqliteStorageFeature : IShellFeature, IInfersDependenciesFrom<StorageFeature>
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IStorageProvider, SqliteStorageProvider>();
    }
}
```

---

## `IConfigurableFeature<T>` — Strongly-Typed Configuration

Implement this interface to receive configuration options automatically bound from the shell's `IConfiguration`.

```csharp
public class DatabaseOptions
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeout { get; set; } = 30;
}

[ShellFeature("Database")]
public class DatabaseFeature : IShellFeature, IConfigurableFeature<DatabaseOptions>
{
    private DatabaseOptions _options = new();

    public void Configure(DatabaseOptions options)
    {
        _options = options;  // Called automatically after config binding
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlServer(_options.ConnectionString));
    }
}
```

The configuration section name is derived from the feature name. See [Feature Configuration](Feature-Configuration) for full details.

---

## `IPostConfigureShellServices` — Post-Configuration Hook

Implement this on a feature to run code after **all** features have configured their services but before the shell's `IServiceProvider` is built.

```csharp
[ShellFeature("Messaging")]
public class MessagingFeature : IShellFeature, IPostConfigureShellServices
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register messaging framework (e.g. MassTransit)
        services.AddMassTransit(cfg => { /* base config */ });
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        // Inspect what transport features registered and finalize the config
        // (e.g. pick up registrations added by RabbitMqTransportFeature)
    }
}
```

---

## `ShellFeatureContext` — Rich Construction Context

Inject `ShellFeatureContext` instead of `ShellSettings` when you need access to all discovered feature descriptors at build time.

```csharp
public class ConditionalFeature(ShellFeatureContext context) : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        var isReportingEnabled = context.Settings.EnabledFeatures.Contains("Reporting");

        if (isReportingEnabled)
            services.AddSingleton<IReportDecorator, EnhancedReportDecorator>();

        // Share data with later-running features via the context property bag
        context.Properties["MyKey"] = "some shared value";
    }
}
```

---

## Feature Discovery

CShells scans assemblies at startup for any type that implements `IShellFeature` (or one of its sub-interfaces). No explicit registration is required.

By default all loaded assemblies are scanned. Limit the scan by passing specific assemblies:

```csharp
builder.AddShells([typeof(CoreFeature).Assembly, typeof(WeatherFeature).Assembly]);
```
