# Building Multitenant Web Apps in .NET with CShells

In most multitenant .NET applications, per-tenant behavior accumulates as conditionals. One customer wants Stripe, another wants PayPal, one plan includes fraud detection and another doesn't. The result is `if (tenant == "acme")` branches scattered across controllers, services, and configuration — each one an implicit coupling between business logic and tenant identity.

[CShells](https://github.com/valence-works/cshells) is an open-source .NET library that takes a different approach. Each tenant is modeled as a **shell** — an isolated execution context with its own `IServiceProvider`, its own `IConfiguration`, and its own set of enabled features. Features register services and expose HTTP endpoints. Shells compose features via configuration.

This article walks through building a multitenant payment processing platform using CShells:

- The **Default** tenant uses Stripe for payments and email for notifications.
- The **Acme** tenant uses PayPal and SMS, with fraud detection enabled.
- The **Contoso** tenant uses Stripe, multi-channel notifications, fraud detection, and a reporting dashboard — all under `/contoso/*`.

Each tenant runs in its own DI container. Enabling a feature in one shell does not affect the others. `Program.cs` remains minimal.

---

## The Problem with Traditional Multitenancy

Traditional multitenant .NET apps usually grow into one of two shapes:

**Shape 1: Branching everywhere.** You check the tenant ID in middleware, inject `ITenantContext` into every service, and litter your business logic with tenant-specific conditionals. The code is hard to test, easy to break, and nearly impossible to reason about as the tenant count grows.

**Shape 2: Multiple deployments.** You deploy a separate instance per tenant and manage the configuration drift across environments. Simple at first, expensive at scale.

CShells offers a third option: one deployment, one codebase, but completely isolated tenant execution contexts that compose different behaviors from modular feature blocks.

---

## Core Concepts in Five Minutes

### Shells

A **shell** is a named, isolated execution context. It has its own `IServiceProvider`, its own `IConfiguration`, and its own list of enabled features.

### Features

A **feature** is a modular unit of functionality. It implements `IShellFeature` (for service registration) or `IWebShellFeature` (for services *and* HTTP endpoints). Features declare themselves with the `[ShellFeature]` attribute and can express dependencies on other features.

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
```

Shells are composed from features via configuration:

```json
{
  "Name": "Acme",
  "Features": ["Core", "PayPalPayment", "SmsNotification", "FraudDetection"]
}
```

---

## Setting Up the Project

Install the NuGet packages:

```bash
dotnet add package CShells
dotnet add package CShells.AspNetCore
```

The recommended project layout separates feature definitions from the main application:

```
YourSolution/
├── src/
│   ├── YourApp/                    # Main ASP.NET Core project
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── YourApp.Features/           # Feature class library
│       └── YourApp.Features.csproj # References: CShells.AspNetCore.Abstractions only
```

The feature library references only `CShells.AspNetCore.Abstractions`, keeping it free from implementation-level dependencies. The main app references `CShells` and `CShells.AspNetCore`.

---

## Building the Feature Library

### The Core Feature

`CoreFeature` implements `IWebShellFeature` so it can register services and expose an HTTP endpoint:

```csharp
[ShellFeature("Core", DisplayName = "Core Services")]
public class CoreFeature(ShellSettings shellSettings) : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<ITimeService, TimeService>();
        services.AddSingleton<ITenantInfo>(sp => new TenantInfo
        {
            TenantId = shellSettings.Id.ToString(),
            TenantName = shellSettings.Id.ToString()
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet("", async (HttpContext context) =>
        {
            var tenantInfo = context.RequestServices.GetRequiredService<ITenantInfo>();
            return Results.Json(new { Tenant = tenantInfo.TenantName });
        });
    }
}
```

`ShellSettings` is injected via primary constructor syntax. CShells registers the current shell's settings in its DI container, so features have access to the shell's identity and configuration at service-registration time.

### Payment Processing Features

The `/payments` endpoint is defined in a base class. Concrete features register different `IPaymentProcessor` implementations:

```csharp
// Base class: owns the /payments endpoint, relies on IPaymentProcessor
public abstract class PaymentProcessingFeatureBase : IWebShellFeature
{
    public abstract void ConfigureServices(IServiceCollection services);

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapPost("/payments", async (HttpContext context) =>
        {
            var processor = context.RequestServices.GetRequiredService<IPaymentProcessor>();
            var request = await context.Request.ReadFromJsonAsync<PaymentRequest>();

            // Optionally use fraud detection if it's enabled for this shell
            var fraudDetection = context.RequestServices.GetService<IFraudDetectionService>();
            var fraudResult = fraudDetection?.AnalyzeTransaction(
                request!.Amount, request.Currency, "unknown");

            var result = processor.ProcessPayment(request!.Amount, request.Currency);
            return Results.Json(new { Payment = result, FraudAnalysis = fraudResult });
        });
    }
}

// Concrete feature: just registers the right processor
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

`DependsOn` declares that these features require `Core` to be active. CShells performs topological sorting at startup, so the order features appear in configuration does not matter.

`GetService<IFraudDetectionService>()` returns `null` if `FraudDetection` is not enabled for the current shell. The payment then proceeds without fraud analysis. The same pattern applies anywhere an optional feature's services are consumed.

### Per-Tenant Feature Configuration

Features can carry per-tenant settings inline in the shell definition:

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

The `IConfiguration` injected here is the shell-scoped configuration, not the global app configuration. CShells builds a separate `IConfiguration` for each shell, layering the shell's own settings on top of the root configuration. `FraudDetection:Threshold` can differ per tenant with no cross-shell leakage.

---

## Configuring the Shells

Shell configuration in `appsettings.json`:

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
          { "Name": "FraudDetection", "Threshold": 0.85, "MaxTransactionAmount": 5000 }
        ],
        "Configuration": {
          "WebRouting": { "Path": "acme" }
        }
      },
      {
        "Name": "Contoso",
        "Features": [
          "Core",
          "StripePayment",
          "MultiChannelNotification",
          { "Name": "FraudDetection", "Threshold": 0.6, "MaxTransactionAmount": 25000 },
          "Reporting"
        ],
        "Configuration": {
          "WebRouting": { "Path": "contoso" }
        }
      }
    ]
  }
}
```

When `FraudDetection` is specified as an object rather than a plain string, the extra properties are merged into the shell's `IConfiguration` under the feature's section name. Acme gets `FraudDetection:Threshold = 0.85`; Contoso gets `0.6`. Each shell's configuration is fully isolated.

`WebRouting.Path` controls the URL prefix for that shell's endpoints. The Default shell handles `/`, Acme handles `/acme/*`, and Contoso handles `/contoso/*`.

`Reporting` appears only in Contoso's feature list. The `/reports` route does not exist in other shells — there is nothing to return 403 or hide behind a permission check; the endpoint simply is not registered.

---

## Wiring It All Up

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Discover features from this assembly and from the features library.
// Reads shell configuration from the "CShells" section in appsettings.json.
builder.AddShells([typeof(CoreFeature)]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseRouting();

// Register shell middleware and mount all feature endpoints
app.MapShells();

app.Run();
```

`AddShells` scans the assemblies containing the seed types for all classes decorated with `[ShellFeature]`. `MapShells` registers the per-shell endpoint groups and installs the routing middleware.

---

## Running It

```bash
dotnet run
```

```bash
# Default tenant — Stripe + email
curl http://localhost:5000/
curl -X POST http://localhost:5000/payments \
  -H "Content-Type: application/json" \
  -d '{"amount": 100, "currency": "USD", "customerEmail": "user@example.com"}'

# Acme — PayPal + SMS + fraud detection
curl -X POST http://localhost:5000/acme/payments \
  -H "Content-Type: application/json" \
  -d '{"amount": 6000, "currency": "USD", "customerEmail": "user@acme.com"}'

# Acme fraud check endpoint (not available on Default)
curl -X POST http://localhost:5000/acme/fraud-check \
  -H "Content-Type: application/json" \
  -d '{"amount": 15000, "currency": "USD", "ipAddress": "203.0.113.42"}'

# Contoso reporting (enterprise only)
curl "http://localhost:5000/contoso/reports?startDate=2024-01-01&endDate=2024-12-31"
```

---

## Background Workers Per Shell

`IShellContextScopeFactory` creates a scoped `IServiceProvider` for any shell. This is useful for background workers that need to process data per-tenant:

```csharp
public class TenantSyncWorker(
    IShellHost shellHost,
    IShellContextScopeFactory scopeFactory,
    ILogger<TenantSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var shell in shellHost.AllShells)
            {
                using var scope = scopeFactory.CreateScope(shell);

                // Resolves from THIS shell's DI container
                var syncService = scope.ServiceProvider.GetService<ITenantSyncService>();
                if (syncService != null)
                    await syncService.SyncAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

`scopeFactory.CreateScope(shell)` returns an `IServiceScope` backed by that shell's `IServiceProvider`. If a shell does not have `ITenantSyncService` registered, `GetService` returns `null` and the shell is skipped.

Register the worker globally:

```csharp
builder.Services.AddHostedService<TenantSyncWorker>();
```

---

## Secrets and Environment-Specific Configuration

Shell configuration participates in the standard .NET configuration hierarchy. Environment variables override `appsettings.json` values using the double-underscore separator:

```bash
# Override Contoso's fraud threshold at deploy time
CShells__Shells__2__Features__3__Threshold=0.5

# Or use a fully qualified path
Shells__Contoso__Configuration__FraudDetection__Threshold=0.5
```

For secrets, use .NET User Secrets in development and your secrets manager of choice in production:

```bash
dotnet user-secrets set \
  "CShells:Shells:0:Configuration:Stripe:ApiKey" \
  "sk_test_..."
```

CShells works with any `IConfiguration` provider, so Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, and similar integrations require no CShells-specific configuration.

---

## What You Get

- Each shell has its own `IServiceProvider`.
- Endpoints only exist if the feature is enabled for that shell.
- Configuration is scoped per shell; values do not leak between tenants.
- No tenant branching in application code.

---

## Going Further

- **Runtime shell management** — Add, update, or remove shells at runtime without restarting the application.
- **Custom shell providers** — Load shell configurations from a database, an API, or any `IShellSettingsProvider` implementation.
- **FluentStorage provider** — Load shell configurations from JSON files on disk or cloud blob storage via `CShells.Providers.FluentStorage`.
- **FastEndpoints integration** — The `CShells.FastEndpoints` package adds shell-awareness to FastEndpoints-based applications.
- **Strongly-typed feature options** — Implement `IConfigurableFeature<TOptions>` for validated, typed configuration with DataAnnotations or FluentValidation.

---

The sample used in this article is available in the repository under `samples/CShells.Workbench`.
