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
