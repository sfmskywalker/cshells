# CShells.Management.Api

Optional REST management API for CShells. Maps a small set of root-level
Minimal API endpoints onto an existing `IEndpointRouteBuilder` so you can
reload shells, observe drain progress, and force-terminate stuck drains
from outside the running process.

## Purpose

Manual testing and demonstration of shell-reload and drain-lifecycle
mechanics over HTTP. Hosts install the endpoints with one line and apply
their own authorization, CORS, rate limiting, etc. by chaining the
standard ASP.NET Core endpoint conventions on the returned
`RouteGroupBuilder`.

> **Manual-testing tool.** This package is intended as a
> developer/operator aid. It applies **no** authorization of its own —
> see "Authorization" below.

## Installation

```bash
dotnet add package CShells.Management.Api
```

The package depends only on `CShells.Abstractions` plus the
`Microsoft.AspNetCore.App` framework reference. It does **not** pull in
`CShells.AspNetCore` or any third-party endpoint stack.

## Usage

```csharp
var app = builder.Build();
app.MapShells();
app.MapShellManagementApi("/_admin/shells");
app.Run();
```

The argument is the route prefix (defaults to `/_admin/shells`).
`MapShellManagementApi` returns a `RouteGroupBuilder` so you can chain
any standard ASP.NET Core endpoint convention.

## Endpoints

Under the configured prefix:

| Verb   | Route                | Purpose                                                            |
|--------|----------------------|--------------------------------------------------------------------|
| GET    | `/`                  | Paginated list of all shells (catalogue + active-gen state).       |
| GET    | `/{name}`            | Focused view: blueprint + every live generation + per-gen drain.   |
| GET    | `/{name}/blueprint`  | Registered blueprint (incl. `ConfigurationData`) without activating.|
| POST   | `/reload/{name}`     | Reload a single shell; returns new generation + drain snapshot.    |
| POST   | `/reload-all`        | Reload every active shell; per-shell outcomes returned as an array. Optional `?maxDegreeOfParallelism=N`. |
| POST   | `/{name}/force-drain`| Force every in-flight drain on the shell to terminate; returns array of `DrainResult`. |

All non-2xx responses use RFC 7807 problem-details bodies.

## Authorization

The endpoints expose direct control over the registry — reloading
shells, forcing drains, and **returning the registered
`ConfigurationData` of every shell verbatim** (which may contain secrets
your shell configuration includes, like connection strings or API keys).
You **must** gate them with your host's authorization scheme before
exposing them on any non-localhost interface.

`MapShellManagementApi` returns a `RouteGroupBuilder` — chain
`RequireAuthorization`, `RequireCors`, `RequireRateLimiting`, or any
endpoint convention on the result:

```csharp
app.MapShellManagementApi("/_admin/shells")
   .RequireAuthorization("AdminOnly")
   .WithTags("CShells Management")
   .AddEndpointFilter(new AuditEndpointFilter());
```

The package itself applies **no** authorization, authentication, or
rate-limit policy — by design, so it composes with whatever auth scheme
your host already runs. The trade-off is that an unprotected install is
a foot-gun outside dev environments.

## What's intentionally missing

- Endpoints for unregistering or mutating blueprints over HTTP.
- WebSocket-based lifecycle event streaming.
- Framework-generated OpenAPI documents — chain `.WithOpenApi()` on the
  install method's return value if you want OpenAPI generation.

Hosts that need any of those build them on top of `IShellRegistry`
directly.

## License

MIT — see [LICENSE](https://github.com/sfmskywalker/cshells/blob/main/LICENSE).
