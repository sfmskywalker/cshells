# Quickstart: Shell Management REST API

**Feature**: [009-management-api](spec.md)
**Audience**: host developers integrating the new optional package
`CShells.Management.Api`. The package exposes six HTTP endpoints over
your `IShellRegistry` so you can reload shells, observe drains, and
force-terminate stuck drains from outside the running process.

> **Manual-testing tool.** This package is intended to be a
> developer/operator tool for manual testing of shell-reload and drain
> mechanics. It does **not** apply authorization of its own — see
> step 4 before exposing it on a non-localhost interface.

## 1. Reference the package

Add a `<PackageReference>` to your host project:

```xml
<ItemGroup>
  <PackageReference Include="CShells.Management.Api" />
</ItemGroup>
```

The package depends only on `CShells.Abstractions` plus the
`Microsoft.AspNetCore.App` framework reference. It does **not** pull in
`CShells.AspNetCore` or any third-party endpoint stack — you can mix it
with vanilla MVC, Minimal APIs, FastEndpoints, Carter, or anything else.

## 2. Install the endpoints

In `Program.cs`, after `app.MapShells()` (or your equivalent
endpoint-mapping line):

```csharp
var app = builder.Build();
app.MapShells();
app.MapShellManagementApi("/_admin/shells");
app.Run();
```

The argument is the route prefix; default is `"/_admin/shells"`. Pick
whatever fits your app's URL conventions.

`MapShellManagementApi` returns a `RouteGroupBuilder` so you can chain
any standard ASP.NET Core endpoint convention (see step 4).

## 3. Try it

With the host running, hit the endpoints with `curl`:

```bash
# List all shells (paginated)
curl http://localhost:5000/_admin/shells/

# Get a focused view including all generations and per-generation drain state
curl http://localhost:5000/_admin/shells/acme

# Get just the registered blueprint without activating
curl http://localhost:5000/_admin/shells/acme/blueprint

# Reload a single shell
curl -X POST http://localhost:5000/_admin/shells/reload/acme

# Reload every active shell, serial (for reproducing contention bugs)
curl -X POST 'http://localhost:5000/_admin/shells/reload-all?maxDegreeOfParallelism=1'

# Force every in-flight drain on a shell to terminate
curl -X POST http://localhost:5000/_admin/shells/acme/force-drain
```

All responses are JSON; the `Content-Type` is `application/json`.
Non-2xx responses follow RFC 7807 problem-details.

## 4. Apply authorization (required for non-localhost deployments)

The endpoints expose direct control over the registry: reloading shells,
forcing drains, returning the registered `ConfigurationData` of every
shell verbatim. You **must** gate them with your host's authorization
scheme before exposing them on any interface that isn't your own
laptop.

`MapShellManagementApi` returns a `RouteGroupBuilder` — chain
`RequireAuthorization`, `RequireCors`, `RequireRateLimiting`, or any
other endpoint convention on the result:

```csharp
app.MapShellManagementApi("/_admin/shells")
   .RequireAuthorization("AdminOnly")        // ← your authorization policy
   .WithTags("CShells Management")
   .AddEndpointFilter(new AuditEndpointFilter());
```

The package applies **no** authorization, authentication, or rate-limit
policy of its own. This is deliberate — it lets the package compose
cleanly with any auth scheme. The trade-off is that an unprotected
install is a foot-gun outside dev environments.

## 5. Observe a drain in real time

Drain observability is the headline feature of this module. Trigger a
reload, then poll the focused-view endpoint to watch the previous
generation move through `Deactivating → Draining → Drained`:

```bash
# Trigger reload
curl -X POST http://localhost:5000/_admin/shells/acme/reload/acme

# Immediately poll — typically you'll see two generations: the new active one,
# and the previous still-draining one
watch -n 1 'curl -s http://localhost:5000/_admin/shells/acme | jq .generations'
```

You'll see output like:

```json
[
  { "generation": 2, "state": "Active",   "drain": null },
  { "generation": 1, "state": "Draining", "drain": { "status": "Pending", "deadline": "..." } }
]
```

The previous-gen entry's `drain.status` advances `Pending → Completed`
(or `TimedOut` / `Forced`) before the entry disappears entirely as the
generation reaches `Disposed`.

## 6. Force a stuck drain

If you've been testing a slow / hung drain handler and the previous
generation is stuck:

```bash
curl -X POST http://localhost:5000/_admin/shells/acme/force-drain
```

The response is a JSON array — one entry per forced generation. If two
reloads back-to-back left two generations draining, both are forced
concurrently and you'll see two array entries.

The endpoint awaits each drain to reach a terminal state before
responding (per-grace-period). For most manual-testing setups (short
or zero grace), the response returns within a fraction of a second.

If the shell has no in-flight drain when you call force-drain, you get
a `404 Not Found` — there's nothing to force.

## 7. What's exposed

| HTTP | Route | Purpose |
|---|---|---|
| `GET` | `/` | Paginated list of all shells (catalogue + active-gen state). |
| `GET` | `/{name}` | Focused view: blueprint + all live generations + per-generation drain snapshots. |
| `GET` | `/{name}/blueprint` | The registered blueprint (incl. `ConfigurationData` verbatim) without activating. |
| `POST` | `/reload/{name}` | Reload a single shell; response carries new generation + drain snapshot on the previous gen. |
| `POST` | `/reload-all` | Reload every active shell; per-shell outcomes in a JSON array. Optional `?maxDegreeOfParallelism=N`. |
| `POST` | `/{name}/force-drain` | Force every in-flight drain on a shell to terminate; returns array of per-generation `DrainResult`. |

The endpoint contracts are documented in detail in
`contracts/ManagementApi.md`.

## 8. What's intentionally missing

Per spec FR-017, the package does **not** offer:

- Endpoints for unregistering blueprints or mutating blueprints over HTTP.
- WebSocket-based lifecycle event streaming.
- Framework-generated OpenAPI documents — chain `.WithOpenApi()` on the
  install method's return value if you want OpenAPI generation, and the
  framework's standard inference will handle the typed responses.

Hosts that need any of those build them on top of `IShellRegistry`
directly (or in a follow-up feature).
