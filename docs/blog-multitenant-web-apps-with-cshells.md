# Building Multitenant Web Apps in .NET with CShells

In many multitenant .NET systems, tenant-specific behavior accumulates as conditional logic. One tenant uses Stripe, another uses PayPal. One plan enables fraud detection, another does not. Over time, `if (tenant == "...")` checks appear in controllers, services, and configuration code. Tenant identity becomes entangled with business logic.

[CShells](https://github.com/valence-works/cshells) takes a different approach. Each tenant is modeled as a **shell** — an isolated execution context with its own `IServiceProvider`, its own `IConfiguration`, and its own set of enabled features. Features register services and optionally expose HTTP endpoints. Shells enable features through configuration.

This article builds a simple payment platform using CShells:

- **Default** uses Stripe and email notifications.
- **Acme** uses PayPal, SMS, and fraud detection.
- **Contoso** uses Stripe, multi-channel notifications, fraud detection, and reporting under `/contoso/*`.

Each tenant runs in its own container. Enabling a feature in one shell does not affect the others.

---

## Traditional Multitenancy Patterns

Multitenant applications typically evolve in one of two directions.

**Approach 1: conditional logic in the application.**  
Tenant context is injected widely. Services branch on tenant ID. Feature flags become hard-coded checks. The system works, but tenant identity leaks into domain logic and spreads.

**Approach 2: per-tenant deployments.**  
Each tenant runs in a separate instance. Behavior is isolated operationally instead of logically. This reduces cross-tenant coupling at the code level but increases deployment and configuration overhead.

CShells keeps a single deployment while isolating tenants at the container and configuration level.

---

## Core Concepts

### Shell

A **shell** is a named execution context with:

- Its own `IServiceProvider`
- Its own `IConfiguration`
- A set of enabled features

Each shell builds its own container at startup.

### Feature

A **feature** is a modular unit of functionality. It implements:

- `IShellFeature` for service registration
- `IWebShellFeature` for services and endpoint mapping

Features are discovered via the `[ShellFeature]` attribute.

```csharp
[ShellFeature("Core", DisplayName = "Core Services")]
public class CoreFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<ITimeService, TimeService>();
    }
}
````

Shells enable features in configuration:

```json
{
  "Name": "Acme",
  "Features": ["Core", "PayPalPayment", "SmsNotification", "FraudDetection"]
}
```

---

## Project Structure

Install the packages:

```bash
dotnet add package CShells
dotnet add package CShells.AspNetCore
```

A common structure separates feature definitions from the host:

```
YourSolution/
├── src/
│   ├── YourApp/
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── YourApp.Features/
│       └── YourApp.Features.csproj
```

The feature library references `CShells.AspNetCore.Abstractions`.
The host references `CShells` and `CShells.AspNetCore`.

This keeps feature definitions independent from host implementation details.

---

## Implementing Features

### Core Feature

`CoreFeature` registers shared services and exposes a root endpoint:

```csharp
[ShellFeature("Core", DisplayName = "Core Services")]
public class CoreFeature(ShellSettings settings) : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<ITimeService, TimeService>();
        services.AddSingleton<ITenantInfo>(_ => new TenantInfo
        {
            TenantId = settings.Id.ToString(),
            TenantName = settings.Id.ToString()
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet("/", (ITenantInfo tenant) =>
            Results.Json(new { Tenant = tenant.TenantName }));
    }
}
```

`ShellSettings` is resolved from the shell container, so feature instances can access the shell identity during registration.

---

### Payment Processing

The `/payments` endpoint is defined in a base class. Concrete features register different processors.

```csharp
public abstract class PaymentProcessingFeatureBase : IWebShellFeature
{
    public abstract void ConfigureServices(IServiceCollection services);

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapPost("/payments", async (HttpContext context) =>
        {
            var processor = context.RequestServices.GetRequiredService<IPaymentProcessor>();
            var request = await context.Request.ReadFromJsonAsync<PaymentRequest>();

            var fraud = context.RequestServices.GetService<IFraudDetectionService>();
            var fraudResult = fraud?.AnalyzeTransaction(
                request!.Amount, request.Currency, "unknown");

            var result = processor.ProcessPayment(request!.Amount, request.Currency);

            return Results.Json(new { Payment = result, Fraud = fraudResult });
        });
    }
}

[ShellFeature("StripePayment", DependsOn = ["Core"])]
public class StripePaymentFeature : PaymentProcessingFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IPaymentProcessor, StripePaymentProcessor>();
}

[ShellFeature("PayPalPayment", DependsOn = ["Core"])]
public class PayPalPaymentFeature : PaymentProcessingFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IPaymentProcessor, PayPalPaymentProcessor>();
}
```

`DependsOn` ensures `Core` is initialized first. Feature order in configuration does not matter.

Optional features are consumed via `GetService<T>()`. If fraud detection is not enabled for a shell, the payment proceeds without it.

---

### Per-Tenant Feature Configuration

Features can bind options from the shell-scoped configuration:

```csharp
[ShellFeature("FraudDetection", DependsOn = ["Core"])]
public class FraudDetectionFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<FraudDetectionOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                config.GetSection("FraudDetection").Bind(options);
            });

        services.AddSingleton<IFraudDetectionService, FraudDetectionService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapPost("/fraud-check", async (HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<IFraudDetectionService>();
            var request = await context.Request.ReadFromJsonAsync<FraudCheckRequest>();

            return Results.Json(service.AnalyzeTransaction(
                request!.Amount, request.Currency, request.IpAddress));
        });
    }
}
```

Each shell receives its own `IConfiguration` instance. Configuration values do not leak between shells.

---

## Shell Configuration

Example configuration:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": ["Core", "StripePayment", "EmailNotification"],
        "Configuration": {
          "WebRouting": { "Path": "" }
        }
      },
      {
        "Name": "Acme",
        "Features": [
          "Core",
          "PayPalPayment",
          "SmsNotification",
          { "Name": "FraudDetection", "Threshold": 0.85 }
        ],
        "Configuration": {
          "WebRouting": { "Path": "acme" }
        }
      }
    ]
  }
}
```

If a feature entry is an object rather than a string, its properties are merged into the shell configuration under the feature's section.

`WebRouting.Path` determines the URL prefix for that shell.

---

## Host Setup

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddShells([typeof(CoreFeature)]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseRouting();

app.MapShells();

app.Run();
```

`AddShells` scans assemblies for `[ShellFeature]` types.
`MapShells` registers middleware and mounts endpoints per shell.

Startup time increases with the number of shells and enabled features since each shell builds its own container.

---

## Background Processing Per Shell

`IShellContextScopeFactory` creates a scoped provider for a given shell:

```csharp
public class TenantSyncWorker(
    IShellHost shellHost,
    IShellContextScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var shell in shellHost.AllShells)
            {
                using var scope = scopeFactory.CreateScope(shell);

                var sync = scope.ServiceProvider.GetService<ITenantSyncService>();
                if (sync != null)
                    await sync.SyncAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

Each scope resolves services from the correct shell container.

---

## Summary

* Each shell has its own `IServiceProvider`.
* Endpoints only exist if the corresponding feature is enabled.
* Configuration is scoped per shell.
* Tenant identity does not appear in domain logic.

The sample used in this article is available under `samples/CShells.Workbench` in the repository.

```
