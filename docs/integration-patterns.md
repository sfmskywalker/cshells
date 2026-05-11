# Integration Patterns

This guide explains how to integrate CShells into existing ASP.NET Core applications without route conflicts.

## Overview

CShells registers shell-specific endpoints dynamically via `IWebShellFeature.MapEndpoints`. When integrating into an existing app, you need to ensure shell routes don't collide with host routes.

## Pattern 1: Dedicated Path Prefixes (Recommended)

Give each shell a unique path prefix:

```json
{
  "CShells": {
    "Shells": {
      "Acme": { "Configuration": { "WebRouting": { "Path": "tenants/acme" } }  },
      "Contoso": { "Configuration": { "WebRouting": { "Path": "tenants/contoso" } }  }
    }
  }
}
```

Result:

- Shell routes: `/tenants/acme/*`, `/tenants/contoso/*`
- Host routes: `/api/*`, `/admin/*`, `/`
- No conflicts.

## Pattern 2: Subdomain-Based Isolation

```json
{
  "CShells": {
    "Shells": {
      "Acme": { "Configuration": { "WebRouting": { "Host": "acme.example.com" } }  }
    }
  }
}
```

Result: `acme.example.com` routes to the Acme shell; `www.example.com` routes to the host app.

## Pattern 3: Mixed Host + Shell Routes

```csharp
var app = builder.Build();

// Host routes first
app.MapGet("/", () => "Host home page");
app.MapControllers();

// Shell routes second
app.MapShells();
```

Register host routes **before** `MapShells()` to avoid shadowing.

## Pattern 4: Root-Level Shell

Set `Path` to `""` when the **entire** application is multi-tenant:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Configuration": { "WebRouting": { "Path": "" } }
      }
    }
  }
}
```

> **Warning:** shell routes will match all root-level requests. Only use this when you have no host-specific routes.

## Excluding Paths from Shell Resolution

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithWebRouting(options =>
    {
        options.ExcludePaths = ["/health", "/swagger", "/admin"];
    });
});
```

Requests to excluded prefixes bypass shell resolution entirely.

## Middleware Ordering

```csharp
var app = builder.Build();

app.UseExceptionHandler("/Error");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Host routes
app.MapControllers();
app.MapHealthChecks("/health");

// Shell routes
app.MapShells();
```

## Complete Example

```csharp
using CShells.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.FromAssemblies(typeof(MyFeature).Assembly);
    cshells.WithSharedAssemblies("Elsa", "Elsa.*");
    cshells.WithSharedAssembliesWhere(name =>
        name.StartsWith("MyFramework.", StringComparison.OrdinalIgnoreCase));
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

// Host routes
app.MapGet("/", () => "Host home");
app.MapControllers();
app.MapHealthChecks("/health");

// Shell routes (resolved by path prefix, host, or header)
app.MapShells();

app.Run();
```

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `AmbiguousMatchException` | Multiple shells share the same route | Give each shell a unique `WebRouting:Path` |
| Host routes return 404 | A root-level shell (`Path: ""`) shadows them | Add path exclusions or move shells under a prefix |
| Shell routes return 404 | `MapShells()` not called, or features not discovered | Verify `MapShells()` is in the pipeline and features are scanned |

## Shared Assembly Selectors

Use root `CShells:SharedAssemblies` configuration or code-first builder calls when an integration package needs to share a framework family:

```json
{
  "CShells": {
    "SharedAssemblies": [
      "Elsa",
      "Elsa.*"
    ]
  }
}
```

`Elsa` is an exact assembly simple-name match. `Elsa.*` is a prefix match against simple names only. The wildcard is only valid as the final character; suffix or contains-style patterns such as `*.Contracts` are intentionally invalid.

Prefer narrow patterns for framework contracts and common infrastructure. Avoid broad patterns that include shell-specific implementation assemblies, because sharing those assemblies can weaken shell isolation.

## Best Practices

- Use dedicated path prefixes (`/tenants/*`, `/apps/*`) for shells
- Register host routes **before** `MapShells()`
- Use `ExcludePaths` to protect health checks, Swagger, and admin routes
- Monitor logs for path-conflict warnings
