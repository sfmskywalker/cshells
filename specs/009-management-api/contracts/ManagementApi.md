# Contract: Shell Management API HTTP endpoints

**Feature**: [009-management-api](../spec.md)
**Package**: `CShells.Management.Api`
**Install**: `app.MapShellManagementApi(prefix = "/_admin/shells")` returns `RouteGroupBuilder`

This document is the authoritative HTTP contract for the six routes
registered by `MapShellManagementApi`. All routes are mapped onto the
`RouteGroupBuilder` returned by `MapGroup(prefix)`; the prefix defaults
to `/_admin/shells` and is configurable per install. The host applies its
own authorization, CORS, rate-limiting, OpenAPI, and endpoint-filter
conventions on the returned builder.

The package contributes **no** service registration in DI. Routes resolve
`IShellRegistry` from `HttpContext.RequestServices` (root scope).

JSON encoding follows System.Text.Json `JsonSerializerDefaults.Web`
(camelCase). All non-2xx responses use RFC 7807 problem-details bodies
via `Results.Problem(...)`.

---

## `GET /` — list shells (paginated)

**Query parameters** (all optional):

| Name | Type | Default | Notes |
|---|---|---|---|
| `cursor` | string | null | Opaque pagination cursor returned by a prior page. |
| `pageSize` | int | 100 | Forwarded to `ShellListQuery.Limit`. Out-of-range → 400. |

**Response 200**:

```json
{
  "items": [
    {
      "name": "acme",
      "blueprint": {
        "name": "acme",
        "features": ["Core", "Billing"],
        "configurationData": { "WebRouting:Path": "acme", "Plan": "Enterprise" }
      },
      "active": {
        "generation": 1,
        "state": "Active",
        "createdAt": "2026-04-27T15:30:00Z",
        "drain": null
      }
    },
    {
      "name": "tenant-x",
      "blueprint": { "name": "tenant-x", "features": ["Core"], "configurationData": {} },
      "active": null
    }
  ],
  "nextCursor": "...",
  "pageSize": 100
}
```

**Errors**: 503 (provider unavailable / shutdown), 400 (pageSize out of
range).

---

## `GET /{name}` — focused view

**Response 200**:

```json
{
  "name": "acme",
  "blueprint": {
    "name": "acme",
    "features": ["Core", "Billing"],
    "configurationData": { "WebRouting:Path": "acme" }
  },
  "generations": [
    {
      "generation": 2,
      "state": "Active",
      "createdAt": "2026-04-27T15:31:00Z",
      "drain": null
    },
    {
      "generation": 1,
      "state": "Draining",
      "createdAt": "2026-04-27T15:30:00Z",
      "drain": { "status": "Pending", "deadline": "2026-04-27T15:31:30Z" }
    }
  ]
}
```

`generations` is the array `IShellRegistry.GetAll(name)` returns (active +
non-active generations). Each entry's `drain` is populated when the
generation's state is `Deactivating`, `Draining`, or `Drained` (per
FR-005). Order is implementation-defined; consumers should not rely on
`generations[0]` being the active gen.

If the name has no registered blueprint **and** no live generations:
**404 Not Found**.

If the name has a registered blueprint but no active generation,
`generations` is empty and `blueprint` is populated.

---

## `GET /{name}/blueprint` — fetch blueprint without activating

**Response 200**:

```json
{
  "name": "acme",
  "features": ["Core", "Billing"],
  "configurationData": { "WebRouting:Path": "acme", "Plan": "Enterprise" }
}
```

`configurationData` is included verbatim per FR-012a. **The host MUST
apply authorization on the install method's `RouteGroupBuilder` before
exposing this endpoint to untrusted callers**, as configuration values
may contain secrets.

**Errors**: 404 (unknown name), 503 (provider unavailable).

---

## `POST /reload/{name}` — reload a single shell

**Response 200**:

```json
{
  "name": "acme",
  "success": true,
  "newShell": {
    "generation": 2,
    "state": "Active",
    "createdAt": "2026-04-27T15:31:00Z",
    "drain": null
  },
  "drain": { "status": "Pending", "deadline": "2026-04-27T15:31:30Z" },
  "error": null
}
```

`drain` (top-level) is the snapshot of the in-flight drain on the
**previous** generation (the one this reload kicked off). `newShell.drain`
is null because the new generation is `Active`.

On failure (e.g., a misconfigured initializer), the registry's
`ReloadAsync` returns a `ReloadResult` whose `Error` is populated; the
endpoint returns **200 OK** with `success: false` and the error
description in the body. This mirrors `POST /reload-all`'s per-entry
contract (a single shell is just `n=1`).

**Errors**: 404 (no blueprint for name), 503 (provider unavailable /
shutdown).

---

## `POST /reload-all` — reload every active shell

**Query parameters**:

| Name | Type | Default | Range | Notes |
|---|---|---|---|---|
| `maxDegreeOfParallelism` | int | 8 | `[1, 64]` | Forwarded to `ReloadOptions.MaxDegreeOfParallelism`. Out-of-range or non-integer → 400. |

**Response 200**:

```json
[
  {
    "name": "acme",
    "success": true,
    "newShell": { "generation": 2, "state": "Active", "createdAt": "...", "drain": null },
    "drain": { "status": "Pending", "deadline": "..." },
    "error": null
  },
  {
    "name": "broken-tenant",
    "success": false,
    "newShell": null,
    "drain": null,
    "error": { "type": "ShellBlueprintUnavailableException", "message": "Source unreachable: ..." }
  }
]
```

The HTTP status is **always 200 OK** when the batch executed (even with
per-entry failures). Per-shell failures appear in each entry's `error`
field. Empty active set → `200 OK` with `[]`.

**Errors**: 400 (parallelism out of range or non-integer), 503 (host
shutdown cancellation).

---

## `POST /{name}/force-drain` — force in-flight drains for a shell name

Walks `IShellRegistry.GetAll(name)`, filters to shells whose `State` is
`Deactivating` or `Draining`, calls `IShell.Drain.ForceAsync(ct)` followed
by `IShell.Drain.WaitAsync(ct)` on each in parallel, and returns the
resulting `DrainResult` array.

**Response 200**:

```json
[
  {
    "name": "acme",
    "generation": 1,
    "status": "Forced",
    "scopeWaitElapsed": "00:00:00.0500000",
    "abandonedScopeCount": 0,
    "handlerResults": [
      { "handlerType": "MyDrainHandler", "outcome": "Cancelled", "elapsed": "00:00:00.1000000", "errorMessage": null }
    ]
  }
]
```

If multiple non-active generations were in flight, the array contains one
entry per forced generation (order is implementation-defined). Each
entry's `status` is one of `Forced`, `Completed` (if drain finished
naturally between request arrival and forcing), or `TimedOut` (rare —
handler-side timeout while we were waiting). `Pending` cannot appear —
`WaitAsync` only returns once a terminal state is reached.

**Errors**:

- `404 Not Found` — unknown shell name (no `GetAll(name)` entries at all).
- `404 Not Found` — known name but no in-flight (`Deactivating`/`Draining`)
  generation. Problem-details body distinguishes "no drain to force" from
  the unknown-name case via the `detail` text.
- `503 Service Unavailable` — host shutdown cancelled the wait.

---

## Error response format

All non-2xx responses use RFC 7807 problem-details bodies:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "No blueprint registered for shell 'acme'.",
  "instance": "/_admin/shells/reload/acme"
}
```

The `detail` field is constructed from the registry exception's `Message`
(or a problem-specific descriptive string for the 404-no-drain-to-force
case). The package does not expose stack traces.

---

## Status mapping reference

(Mirrors `spec.md` FR-013 for ease of test cross-referencing.)

| Condition | Status |
|---|---|
| `ShellBlueprintNotFoundException` raised | 404 Not Found |
| `ShellBlueprintUnavailableException` raised | 503 Service Unavailable |
| `OperationCanceledException` from host shutdown | 503 Service Unavailable |
| `ArgumentOutOfRangeException` (parallelism / pageSize) | 400 Bad Request |
| Force-drain on name with no in-flight (`Deactivating`/`Draining`) gen | 404 Not Found |
| Per-shell failure inside `reload-all` | 200 OK; entry's `error` populated |

---

## Composition with endpoint conventions

The `RouteGroupBuilder` returned by `MapShellManagementApi` is a standard
ASP.NET Core type. All conventions chain uniformly across the six routes:

```csharp
app.MapShellManagementApi("/_admin/shells")
   .RequireAuthorization("AdminOnly")
   .WithTags("CShells Management")
   .AddEndpointFilter(new MyAuditFilter())
   .WithOpenApi();
```

`AddEndpointFilter`, `RequireCors`, `RequireRateLimiting` etc. all work
the same way. The package itself applies no conventions of its own.
