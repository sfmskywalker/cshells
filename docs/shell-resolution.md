# Shell Resolution

Shell resolution determines which shell handles an incoming HTTP request. CShells provides a configurable resolver pipeline.

## Overview

When a request arrives, the `ShellMiddleware` runs it through an ordered list of `IShellResolverStrategy` implementations. The first strategy that returns a match wins. If none match, the `DefaultShellResolverStrategy` falls back to the shell named `"Default"`.

Important: the middleware gives precedence to the shell that owns a matched endpoint (via `ShellEndpointMetadata` applied to endpoints). If routing has selected an endpoint that was registered by a shell, that endpoint will execute inside the service scope of the shell that registered it — even if the resolver pipeline would otherwise pick a different shell. For this to work the routing middleware must run before the shell middleware (i.e., `UseRouting()` / endpoint routing must be in place before the shell middleware).

## Default Resolver: WebRoutingShellResolver

The built-in `WebRoutingShellResolver` supports multiple resolution methods. The methods are attempted in the following order (first match wins):

1. **Path** (if `EnablePathRouting` is enabled and the request path contains a path segment)
2. **Host** (if `EnableHostRouting` is enabled and a host matches a shell `WebRouting:Host`)
3. **Header** (if `HeaderName` is set; read a header value and match)
4. **Claim** (if `ClaimKey` is set; read a user claim and match)
5. **Root path fallback** (a shell explicitly configured with `WebRouting:Path = ""` acts as a root-level fallback; this is only considered after all other methods)

Note: path-based resolution is attempted first by the unified resolver. The resolver also treats an explicit empty-string `WebRouting:Path` (i.e. `""`) specially as a root-level fallback and only considers it after other matching strategies have been tried.

### Important configuration details and gotchas

- Path values in shell configuration must not start with a leading slash. If a shell's `WebRouting:Path` starts with a `/` the resolver will throw a configuration exception. Specify `"acme"` not `"/acme"`.

- Exclude paths (the `ExcludePaths` option) are matched against the incoming request path which includes the leading `/`. Use prefixes that start with `/` (for example: `"/health"`, `"/swagger"`).

- Header- and claim-based routing require that each shell explicitly opt in by setting the corresponding key in its shell configuration. In other words:
  - You may set a global option `HeaderName` (or `ClaimKey`) so the resolver knows which header/claim to examine, but each shell must have `WebRouting:HeaderName` (or `WebRouting:ClaimKey`) set to that same value to indicate it supports routing by that header/claim.
  - When both the global option and the shell's `WebRouting:HeaderName`/`WebRouting:ClaimKey` match, the resolver compares the header/claim value to the shell's id/name to find a match.

  Example (header-based):
  ```json
  {
    "CShells": {
      "Shells": [
        {
          "Name": "Acme",
          "Configuration": { "WebRouting": { "HeaderName": "X-Tenant-Id" } }
        }
      ]
    }
  }
  ```
  And the resolver options:
  ```csharp
  shells.WithWebRouting(options => options.HeaderName = "X-Tenant-Id");
  ```
  A request with header `X-Tenant-Id: Acme` will route to the `Acme` shell.

- If more than one shell is explicitly configured with `WebRouting:Path = ""` (an explicit empty string), the resolver treats that as ambiguous and returns `null` so that the `DefaultShellResolverStrategy` can act as the final fallback.

### WebRoutingShellResolverOptions

| Property             | Type       | Default | Description                                      |
|----------------------|------------|---------|--------------------------------------------------|
| `EnablePathRouting`  | `bool`     | `true`  | Resolve shells by URL path prefix                |
| `EnableHostRouting`  | `bool`     | `true`  | Resolve shells by HTTP `Host` header             |
| `HeaderName`         | `string?`  | `null`  | Global HTTP header to read the shell ID from (shells must opt-in via `WebRouting:HeaderName`) |
| `ClaimKey`           | `string?`  | `null`  | Global user claim key to read the shell ID from (shells must opt-in via `WebRouting:ClaimKey`) |
| `ExcludePaths`       | `string[]?`| `null`  | URL prefixes (include leading `/`) to skip during resolution           |

### Configuration

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithConfigurationProvider(builder.Configuration);
    shells.WithWebRouting(options =>
    {
        options.HeaderName = "X-Tenant-Id";
        options.ClaimKey = "tenant_id";
        options.ExcludePaths = new[] { "/health", "/swagger" };
        options.EnablePathRouting = true;
        options.EnableHostRouting = false;
    });
});
```

> Note: the unified resolver will attempt path-based matching before host/header/claim-based checks. Configure which methods are active via the options.

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

Result: a request with `X-Tenant-Id: Acme` routes to the Acme shell — provided the `Acme` shell's configuration also opts in by setting `WebRouting:HeaderName` to the same header name (see "Important configuration details").

## Claim-Based Routing

```csharp
builder.Services.AddCShells(shells => shells.WithWebRouting(options => options.ClaimKey = "tenant_id"));
```

Result: if the authenticated user has claim `tenant_id = "Acme"`, the request routes to Acme — provided the `Acme` shell's configuration also sets `WebRouting:ClaimKey` to the same value.

## Middleware caching and request-scoped execution

- The shell middleware can cache resolution results when enabled via the shell middleware options. When caching is enabled the cache key is composed from the request host, path and HTTP method (`{Host}:{Path}:{Method}`).

- Null (no-match) results are not cached long-lived; the middleware notes a very short expiration for null results so that the absence of a match is not aggressively cached.

- When a shell is resolved, the middleware creates a service scope from that shell's `IServiceProvider` and sets `HttpContext.RequestServices` to that scope for the duration of the request. The scope is disposed when the request completes.

- Because endpoints compiled by some frameworks (e.g., FastEndpoints) resolve services directly from `HttpContext.RequestServices`, the middleware prefers the shell ID attached to a matched endpoint (if present) so those shell-registered endpoints execute under the correct shell's scope.

## Default Shell Fallback

If no strategy matches, `DefaultShellResolverStrategy` resolves to the shell named `"Default"` (case-insensitive).

## Excluding Paths

```csharp
builder.Services.AddCShells(shells =>
{
    shells.WithWebRouting(options =>
    {
        options.ExcludePaths = new[] { "/health", "/swagger", "/admin" };
    });
});
```

Requests starting with excluded prefixes bypass path-based shell resolution.

## Custom Resolver Strategy

Implement `IShellResolverStrategy` if you need custom behavior.

```csharp
public class ApiKeyShellResolver : IShellResolverStrategy
{
    private readonly IApiKeyStore _store;

    public ApiKeyShellResolver(IApiKeyStore store) => _store = store;

    public ShellId? Resolve(ShellResolutionContext context)
    {
        // custom logic to return a ShellId or null
    }
}
```

Register it with an order to control where it runs relative to the built-in strategies.
