# Building Multitenant Web Apps in .NET with CShells

Multitenancy is one of those problems that looks easy on a whiteboard and brutal in production. Every SaaS product eventually reaches the moment where one customer wants Stripe and another wants PayPal, one plan gets fraud detection and another doesn't, and suddenly your clean codebase is riddled with `if (tenant == "acme")` branches scattered across controllers, services, and configuration files.

There's a better way.

[CShells](https://github.com/valence-works/cshells) is a lightweight, open-source .NET library that brings a *shell and feature* model to ASP.NET Core applications. Instead of branching on tenant identity throughout your code, you model each tenant as a **shell** — an isolated execution context with its own DI container, feature set, and configuration. Features register services and expose endpoints. Shells compose features. Zero `if-tenant` branches required.

In this article, we'll build a multitenant payment processing platform from scratch using CShells. By the end, you'll have a single ASP.NET Core application where:

- The **Default** tenant uses Stripe for payments and email for notifications.
- The **Acme** tenant uses PayPal and SMS, with fraud detection enabled.
- The **Contoso** tenant uses Stripe, multi-channel notifications, fraud detection *and* a reporting dashboard — all behind `/contoso/*`.

Every tenant gets a completely isolated DI container. Enabling a feature for one tenant has zero impact on any other. And `Program.cs` stays clean.

---

## The Problem with Traditional Multitenancy

Traditional multitenant .NET apps usually grow into one of two shapes:

**Shape 1: Branching everywhere.** You check the tenant ID in middleware, inject `ITenantContext` into every service, and litter your business logic with tenant-specific conditionals. The code is hard to test, easy to break, and nearly impossible to reason about as the tenant count grows.

**Shape 2: Multiple deployments.** You deploy a separate instance per tenant and manage the configuration drift across environments. Simple at first, expensive at scale.

CShells offers a third option: one deployment, one codebase, but completely isolated tenant execution contexts that compose different behaviors from modular feature blocks.

---

## Core Concepts in Five Minutes

### Shells

A **shell** is a named, isolated execution context. It has its own `IServiceProvider`, its own `IConfiguration`, and its own list of enabled features. Think of it as a mini-application running inside your application.

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

### Shell Composition

Shells are composed from features in configuration — no code changes needed to add or remove a feature from a tenant:

```json
{
  "Name": "Acme",
  "Features": ["Core", "PayPalPayment", "SmsNotification", "FraudDetection"]
}
```

That's the model. Now let's build something real.

---

## Setting Up the Project

Install the NuGet packages:

```bash
dotnet add package CShells
dotnet add package CShells.AspNetCore
```

The recommended project layout separates feature definitions from the main application. This keeps your feature library lightweight and free from implementation-level dependencies:

```
YourSolution/
├── src/
│   ├── YourApp/                    # Main ASP.NET Core project
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── YourApp.Features/           # Feature class library
│       └── YourApp.Features.csproj # References: CShells.AspNetCore.Abstractions only
```

Your feature library references only `CShells.AspNetCore.Abstractions` — a thin package with no implementation dependencies. Your main app references the full `CShells` and `CShells.AspNetCore` packages.

---

## Building the Feature Library

### The Core Feature

Every shell in our platform needs audit logging, a time service, and a tenant info endpoint. We model this as a `CoreFeature` that implements `IWebShellFeature` so it can register both services and an HTTP endpoint:

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

Notice that `ShellSettings` is injected directly into the feature's constructor using primary constructor syntax. CShells makes the current shell's settings available in the DI container, so each feature instance has access to its shell's identity and configuration at service-registration time.

### Payment Processing Features

We want different tenants to use different payment processors. The key insight: define the *endpoint* in a base class, and let concrete feature subclasses register different service implementations.

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

The `DependsOn` property tells CShells that these features require `Core` to be initialized first. CShells performs topological sorting of features at startup, so order in configuration doesn't matter.

Notice also how `GetService<IFraudDetectionService>()` (nullable) is used instead of `GetRequiredService`. If `FraudDetection` is enabled for this shell, the service resolves. If not, it's `null` and the payment proceeds without fraud analysis. This *graceful degradation* pattern is idiomatic CShells.

### Per-Tenant Feature Configuration

Features can carry per-tenant configuration inline in the shell definition. Our fraud detection feature has a configurable risk threshold and maximum transaction amount:

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

The `IConfiguration` injected here is the *shell-scoped* configuration — not the global app configuration. CShells builds a separate `IConfiguration` for each shell, layering the shell's own settings on top of the root configuration. Each tenant can therefore have different values for `FraudDetection:Threshold` without any tenant being aware of the others' settings.

---

## Configuring the Shells

With features defined, we configure three shells. You can do this in `appsettings.json`:

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

A few things worth calling out here:

**Inline feature configuration.** The `FraudDetection` entry is an object instead of a string, which lets you pass feature-level settings right alongside the feature name. Acme gets a conservative 0.85 risk threshold; Contoso gets a tighter 0.6 threshold with a higher maximum transaction cap. Each shell's `IConfiguration` is built with these values already injected, so `config.GetSection("FraudDetection").Bind(options)` in the feature just works, with no cross-tenant leakage.

**Path-based routing.** The `WebRouting.Path` setting controls which URL prefix routes to each shell. CShells' middleware intercepts requests and dispatches them to the matching shell's endpoint route builder. The Default shell handles `/`, Acme handles `/acme/*`, and Contoso handles `/contoso/*`.

**Tier-based feature gating.** The `Reporting` feature appears only in Contoso's feature list. No code changes required to gate it — it simply doesn't exist in the other shells' DI containers. A `GET /reports` request to `/acme/reports` returns 404 naturally.

---

## Wiring It All Up

`Program.cs` is now minimal — just shell registration and middleware:

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

`AddShells` accepts a seed type used for assembly scanning. CShells discovers all types decorated with `[ShellFeature]` in those assemblies and makes them available for composition. `MapShells` registers the per-shell endpoint groups and installs the routing middleware that dispatches requests to the right shell.

That's the entire host configuration. All business logic lives in features.

---

## Running It

```bash
dotnet run
```

Then try the following requests:

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

Every tenant gets the right processor, the right notification channel, and only the endpoints their subscription unlocks — all from a single running process.

---

## Background Workers Per Shell

Multitenancy doesn't stop at HTTP. CShells provides `IShellContextScopeFactory` for creating scoped service providers within any shell, which is exactly what you need for background workers that process data per-tenant:

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

`scopeFactory.CreateScope(shell)` gives you an `IServiceScope` backed by that shell's service provider. If a shell doesn't have `ITenantSyncService` registered (because it's a premium feature that tenant hasn't enabled), `GetService` returns null and the shell is skipped — same graceful degradation pattern as the HTTP layer.

Register the worker globally:

```csharp
builder.Services.AddHostedService<TenantSyncWorker>();
```

---

## Secrets and Environment-Specific Configuration

Shell configuration follows the standard .NET configuration hierarchy, so environment variables override `appsettings.json` values using a double-underscore separator:

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

CShells integrates with the standard `IConfiguration` pipeline, so Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, or any other configuration provider works out of the box.

---

## What You Get

Let's step back and count what CShells handled for us:

- **Isolated DI containers** — Acme's `PayPalPaymentProcessor` and Contoso's `StripePaymentProcessor` live in completely separate service providers. No risk of cross-tenant service leakage.
- **Feature-gated endpoints** — The `/fraud-check` and `/reports` routes literally don't exist in shells that don't include those features. No 403 responses, no authorization checks for missing features — they're simply absent.
- **Per-tenant configuration** — `FraudDetection:Threshold` is 0.85 for Acme and 0.6 for Contoso. Each shell sees only its own values.
- **Zero `if-tenant` branches** — The payment endpoint doesn't know or care which tenant is running. It resolves `IPaymentProcessor` from its shell's container and calls it. The tenant identity is encoded in the DI registration, not in conditional logic.
- **Clean `Program.cs`** — All business logic is in features. The host configuration is under 20 lines.

---

## Going Further

CShells has more surface area worth exploring:

- **Runtime shell management** — Add, update, or remove shells at runtime without restarting the application. Useful for onboarding new tenants in a running system.
- **Custom shell providers** — Load shell configurations from a database, an API, or any `IShellSettingsProvider` implementation.
- **FluentStorage provider** — Load shell configurations from individual JSON files on disk or in cloud blob storage via the `CShells.Providers.FluentStorage` package.
- **FastEndpoints integration** — The `CShells.FastEndpoints` package brings CShells shell-awareness to FastEndpoints-based applications.
- **Feature validation** — Implement `IConfigurableFeature<TOptions>` to get strongly-typed, automatically validated feature configuration with DataAnnotations or FluentValidation.

---

## Wrapping Up

Multitenancy in .NET doesn't have to mean tangled conditionals, sprawling middleware, or multiple deployments. CShells lets you encode tenant-specific behavior in the composition of features and DI registrations rather than in runtime branching. Each tenant is a shell; each capability is a feature; the host is just glue.

The result is a codebase where adding a new tenant is a JSON change, adding a premium feature is a new class, and reasoning about what Acme can do versus what Contoso can do is as simple as reading their shell definition files.

You can find CShells on [GitHub](https://github.com/valence-works/cshells) and on [NuGet](https://www.nuget.org/packages/CShells). The full working version of the payment platform sample used in this article is included in the repository under `samples/CShells.Workbench`. 

Happy building.
