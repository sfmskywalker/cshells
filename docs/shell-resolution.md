# Shell Resolution

Shell resolution determines which shell handles an incoming HTTP request. CShells provides a configurable resolver pipeline.

## Overview

When a request arrives, the `ShellMiddleware` runs it through an ordered list of `IShellResolverStrategy` implementations. The first strategy that returns a match wins. If none match, the `DefaultShellResolverStrategy` falls back to the shell named `"Default"`.

## Default Resolver: WebRoutingShellResolver

The built-in `WebRoutingShellResolver` supports four resolution methods, tried in order:

1. **Header** (if `HeaderName` is set)
2. **Claim** (if `ClaimKey` is set)
3. **Host** (if any shell has `WebRouting:Host` configured)
4. **Path** (if any shell has `WebRouting:Path` configured)

### WebRoutingShellResolverOptions

| Property             | Type       | Default | Description                                      |
|----------------------|------------|---------|--------------------------------------------------|
| `EnablePathRouting`  | `bool`     | `true`  | Resolve shells by URL path prefix                |
| `EnableHostRouting`  | `bool`     | `true`  | Resolve shells by HTTP `Host` header             |
| `HeaderName`         | `string?`  | `null`  | HTTP header to read the shell ID from            |
| `ClaimKey`           | `string?`  | `null`  | User claim key to read the shell ID from         |
| `ExcludePaths`       | `string[]?`| `null`  | URL prefixes to skip during resolution           |

### Configuration

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithConfigurationProvider(builder.Configuration);
    shells.WithWebRouting(options =>
    {
        options.HeaderName = "X-Tenant-Id";
        options.ClaimKey = "tenant_id";
        options.ExcludePaths = ["/health", "/swagger"];
        options.EnablePathRouting = true;
        options.EnableHostRouting = false;
    });
});
```

## Path-Based Routing

Configure a path prefix per shell in `appsettings.json`:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Configuration": { "WebRouting": { "Path": "" } }
      },
      {
        "Name": "Acme",
        "Configuration": { "WebRouting": { "Path": "acme" } }
      }
    ]
  }
}
```

Result:

- `GET /` → Default shell
- `GET /acme/` → Acme shell
- `GET /acme/posts` → Acme shell

## Host-Based Routing

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Acme",
        "Configuration": { "WebRouting": { "Host": "acme.example.com" } }
      }
    ]
  }
}
```

Result: requests to `acme.example.com` route to the Acme shell.

## Header-Based Routing

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithConfigurationProvider(builder.Configuration);
    shells.WithWebRouting(options => options.HeaderName = "X-Tenant-Id");
});
```

Result: a request with `X-Tenant-Id: Acme` routes to the Acme shell.

## Claim-Based Routing

```csharp
builder.Services.AddCShells(shells => shells.WithWebRouting(options => options.ClaimKey = "tenant_id"));
```

Result: if the authenticated user has claim `tenant_id = "Acme"`, the request routes to Acme.

## Default Shell Fallback

If no strategy matches, `DefaultShellResolverStrategy` resolves to the shell named `"Default"` (case-insensitive).

## Excluding Paths

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithWebRouting(options =>
    {
        options.ExcludePaths = ["/health", "/swagger", "/admin"];
    });
});
```

Requests starting with excluded prefixes bypass shell resolution entirely.

## Custom Resolver Strategy

Implement `IShellResolverStrategy`:

```csharp
public class ApiKeyShellResolver : IShellResolverStrategy
{
    private readonly IApiKeyStore _store;

    public ApiKeyShellResolver(IApiKeyStore store) => _store = store;

    public ValueTask<ShellId?> ResolveAsync(
        HttpContext context, CancellationToken ct = default)
    {
        if (!context.Request.Headers.TryGetValue("X-API-Key", out var key))
            return ValueTask.FromResult<ShellId?>(null);

        var tenantId = _store.GetTenantId(key!);
        return ValueTask.FromResult(
            tenantId is null ? null : new ShellId(tenantId));
    }
}
```

Register it:

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithResolverStrategy<ApiKeyShellResolver>(order: 10);
});
```
