# Shell Resolution

Shell resolution determines which shell handles an incoming HTTP request. CShells ships with a unified `WebRoutingShellResolver` that supports path, host, header, and claim-based routing out of the box — but only **applied** shells participate in routing.

---

## How Resolution Works

For each incoming request, CShells runs through registered resolver strategies in order. The first strategy that returns a shell name wins. Desired-only shells that are deferred or failed do not participate until a runtime has been committed.

```
Request
  └──> WebRoutingShellResolver
         ├── Path routing    (WebRouting:Path match)
         ├── Host routing    (WebRouting:Host match)
         ├── Header routing  (X-Tenant-Id: header)
         └── Claim routing   (tenant_id claim)
  └──> DefaultShellResolverStrategy  (uses explicit `Default` only when it is applied)
```

---

## Default Setup

`AddShells()` registers the web routing resolver automatically. No extra configuration is needed for path and host routing.

```csharp
builder.AddShells();  // WebRoutingShellResolver is registered by default
```

---

## Path-Based Resolution

A shell is resolved when the first URL path segment matches the shell's `WebRouting:Path`.

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Acme",
        "Configuration": {
          "WebRouting": { "Path": "acme" }
        }
      }
    ]
  }
}
```

Requests to `/acme/*` → resolved to the `Acme` shell, provided `Acme` currently has an applied runtime.

Path routing is enabled by default (`EnablePathRouting = true`).

---

## Host-Based Resolution

A shell is resolved when the request `Host` header matches the shell's `WebRouting:Host`.

```json
{
  "Name": "Acme",
  "Configuration": {
    "WebRouting": { "Host": "acme.example.com" }
  }
}
```

Requests with `Host: acme.example.com` → resolved to the `Acme` shell.

Host routing is enabled by default (`EnableHostRouting = true`).

---

## Header-Based Resolution

Configure a custom header name and shells are resolved by reading that header value as the shell name.

```csharp
builder.AddShells(cshells =>
{
    cshells.WithWebRouting(options =>
    {
        options.HeaderName = "X-Tenant-Id";
    });
});
```

A request with `X-Tenant-Id: Acme` → resolved to the `Acme` shell.

---

## Claim-Based Resolution

Resolve the shell from a claim in the authenticated user's identity.

```csharp
builder.AddShells(cshells =>
{
    cshells.WithWebRouting(options =>
    {
        options.ClaimKey = "tenant_id";
    });
});
```

An authenticated request where the user has the claim `tenant_id = Acme` → resolved to the `Acme` shell.

---

## Excluding Paths from Resolution

Prevent shell resolution for specific paths (e.g., `/health`, `/swagger`, `/api`). Requests to excluded paths are not processed by the shell middleware.

```csharp
builder.AddShells(cshells =>
{
    cshells.WithWebRouting(options =>
    {
        options.ExcludePaths = ["/health", "/swagger", "/api"];
    });
});
```

---

## Custom Resolver Strategy

Implement `IShellResolverStrategy` for fully custom resolution logic:

```csharp
using CShells.Resolution;

public class ApiKeyShellResolver : IShellResolverStrategy
{
    private readonly IApiKeyService _apiKeyService;

    public ApiKeyShellResolver(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    public async Task<string?> ResolveShellIdAsync(HttpContext context)
    {
        var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (apiKey is null)
            return null;

        return await _apiKeyService.GetTenantIdAsync(apiKey);
    }
}
```

Register with an optional execution order:

```csharp
builder.AddShells(cshells =>
{
    cshells.WithResolverStrategy<ApiKeyShellResolver>(order: 10);
});
```

A lower `order` value means the strategy runs earlier. The built-in `WebRoutingShellResolver` has order 0; the fallback `DefaultShellResolverStrategy` has order 1000.

---

## Advanced: Full Resolver Pipeline Control

Use `ConfigureResolverPipeline` when you need to replace the default strategies entirely:

```csharp
builder.AddShells(cshells =>
{
    cshells.ConfigureResolverPipeline(pipeline => pipeline
        .Use<ClaimsShellResolver>(order: 0)
        .Use<HeaderShellResolver>(order: 50)
        .UseFallback<DefaultShellResolverStrategy>()
    );
});
```

---

## Non-Web Applications

For non-web applications (e.g., console apps, background services), use `WithDefaultResolver()` to always resolve to the `"Default"` shell:

```csharp
services.AddCShells(cshells =>
{
    cshells.WithDefaultResolver();
});
```

## Explicit `Default` Behavior

If a shell named `Default` is explicitly configured, fallback is strict:

- **Applied `Default`** → fallback can resolve to `Default`
- **Configured but unapplied `Default`** → fallback returns no shell
- **No explicit `Default` configured** → fallback may choose the first applied shell

This prevents a deferred or failed explicit default shell from silently routing traffic to another tenant.

## Applied-Only Endpoints

Endpoint registration follows the same rule as routing:

- applied shells expose endpoints
- deferred / failed desired shells expose no shell-owned endpoints
- runtime status remains inspectable through `IShellRuntimeStateAccessor`

---

## `WebRoutingShellResolverOptions` Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `EnablePathRouting` | `bool` | `true` | Match shells by URL path prefix |
| `EnableHostRouting` | `bool` | `true` | Match shells by `Host` header |
| `HeaderName` | `string?` | `null` | Header to read as shell name |
| `ClaimKey` | `string?` | `null` | Claim key to read as shell name |
| `ExcludePaths` | `string[]?` | `null` | Paths that bypass shell resolution |
