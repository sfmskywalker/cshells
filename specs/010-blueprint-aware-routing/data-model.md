# Phase 1 Data Model: Blueprint-Aware Path Routing

**Feature**: [010-blueprint-aware-routing](spec.md)
**Date**: 2026-04-28

This document specifies the value types and entity relationships introduced by the route index. All names are normative and pin the working names from `spec.md`. Each entry calls out: purpose, fields, lifecycle, invariants, and the source of truth for its data.

## Entities

### `ShellRouteEntry`

A single blueprint's contribution to the route index. Read at index-population time from `IShellBlueprint.Properties["WebRouting"]`. Immutable.

```csharp
namespace CShells.AspNetCore.Routing;

public sealed record ShellRouteEntry(
    string ShellName,
    string? Path,
    string? Host,
    string? HeaderName,
    string? ClaimKey);
```

| Field | Type | Source | Notes |
|---|---|---|---|
| `ShellName` | `string` (non-empty) | `IShellBlueprint.Name` | Identifier the registry will receive in `GetOrActivateAsync`. |
| `Path` | `string?` | `Properties["WebRouting:Path"]` | `null` → not eligible for path mode. Empty string `""` → eligible for **root**-path mode (`TryResolveByRootPath`). Non-empty value MUST NOT begin with `/`; entries violating this are excluded at population time per R-005. |
| `Host` | `string?` | `Properties["WebRouting:Host"]` | `null` → not eligible for host mode. Compared case-insensitively. |
| `HeaderName` | `string?` | `Properties["WebRouting:HeaderName"]` | `null` → not eligible for header mode. Names the header whose value MUST equal `ShellName` for a match (existing `FindMatchingShellByIdentifier` semantics). |
| `ClaimKey` | `string?` | `Properties["WebRouting:ClaimKey"]` | `null` → not eligible for claim mode. Names the claim whose value MUST equal `ShellName` for a match (existing `FindMatchingShellByIdentifier` semantics). |

**Invariants**:
- `ShellName` is non-empty (Guard.Against.NullOrWhiteSpace at construction).
- At least one of `Path`, `Host`, `HeaderName`, `ClaimKey` SHOULD be non-null for an entry to be useful, but the index does NOT reject entries with all four null — they simply do not contribute to any lookup table.
- A `Path` value beginning with `/` is rejected by `ShellRouteIndexBuilder` (entry is skipped for that mode and a warning is logged); this is intentionally enforced at the *builder* layer so the entity itself stays a pure data carrier.

---

### `ShellRouteCriteria`

The routing inputs extracted from a request, supplied by `WebRoutingShellResolver` to `IShellRouteIndex.TryMatchAsync`. Immutable.

```csharp
namespace CShells.AspNetCore.Routing;

public sealed record ShellRouteCriteria(
    string? PathFirstSegment,
    bool IsRootPath,
    string? Host,
    string? HeaderName,
    string? HeaderValue,
    string? ClaimKey,
    string? ClaimValue);
```

| Field | Type | Source | Notes |
|---|---|---|---|
| `PathFirstSegment` | `string?` | URL path's first segment (no leading slash) | `null` when path routing is disabled or the path is root/empty. The string `""` is reserved for the root case and indicated by `IsRootPath` instead. |
| `IsRootPath` | `bool` | request URL is `/` (or empty) and path routing is enabled | When `true`, the index queries the root-path table (`Path = ""` entries) rather than the path-segment lookup. |
| `Host` | `string?` | `HttpContext.Request.Host.Host` | `null` when host routing is disabled. |
| `HeaderName` / `HeaderValue` | `string?` / `string?` | resolver options + request headers | Both null when header routing is disabled. |
| `ClaimKey` / `ClaimValue` | `string?` / `string?` | resolver options + user claims | Both null when claim routing is disabled. |

**Construction rules**:
- The resolver populates only the fields whose corresponding routing mode is enabled in `WebRoutingShellResolverOptions`.
- The resolver has already validated `PathFirstSegment` does not start with `/` before constructing the criteria (current `WebRoutingShellResolver` extracts the segment via `pathValue.IndexOf('/')`).
- `IsRootPath = true` and `PathFirstSegment != null` is invalid; the resolver MUST NOT construct such a criteria.

---

### `ShellRouteMatch`

The optional result of `IShellRouteIndex.TryMatchAsync`. Immutable.

```csharp
namespace CShells.AspNetCore.Routing;

public sealed record ShellRouteMatch(
    ShellId ShellId,
    ShellRoutingMode MatchedMode);

public enum ShellRoutingMode
{
    Path,
    RootPath,
    Host,
    Header,
    Claim,
}
```

| Field | Type | Notes |
|---|---|---|
| `ShellId` | `ShellId` | The matched blueprint's identifier; passed unchanged to `IShellRegistry.GetOrActivateAsync(ShellId.Name, ct)`. |
| `MatchedMode` | `ShellRoutingMode` | Which routing mode produced the match; surfaced in the `Debug`-level match log (R-006) and used by tests for precise assertions. |

**Lifecycle**: produced once per `TryMatchAsync` call, consumed by the resolver, never persisted.

---

### `ShellRouteIndexSnapshot` *(internal)*

The immutable indexed view that `DefaultShellRouteIndex` publishes via `Volatile.Write`. Internal to `CShells.AspNetCore.Routing`; not part of the public abstraction.

```csharp
namespace CShells.AspNetCore.Routing;

internal sealed class ShellRouteIndexSnapshot
{
    public required FrozenDictionary<string, ShellRouteEntry> ByPathSegment { get; init; } // key: case-insensitive
    public required FrozenDictionary<string, ShellRouteEntry> ByHost { get; init; }        // key: case-insensitive
    public required FrozenDictionary<string, ShellRouteEntry> ByHeaderValue { get; init; } // key: case-insensitive (the header VALUE = blueprint name; same shape)
    public required FrozenDictionary<string, ShellRouteEntry> ByClaimValue { get; init; }
    public ShellRouteEntry? RootPathEntry { get; init; } // null = no root-path-eligible blueprint OR ambiguous (>1 claimants)
    public bool RootPathAmbiguous { get; init; }        // distinguishes "no entry" from "ambiguous"
    public required ImmutableArray<ShellRouteEntry> All { get; init; } // for diagnostic logging (R-006)
}
```

| Field | Purpose |
|---|---|
| `ByPathSegment` | Reverse map for `ShellRouteCriteria.PathFirstSegment` lookup. Keys are blueprint short names (because path routing matches the path segment against `WebRouting:Path` which by convention equals the short name when path-routed). |
| `ByHost` | Reverse map for `Host` mode. Keys are full host strings (e.g., `acme.example.com`). |
| `ByHeaderValue` | Reverse map for header-mode lookups. The current resolver compares header value against shell descriptor name (existing `FindMatchingShellByIdentifier` semantics), so the key is the shell name; the entry is the route entry with the matching `HeaderName`. |
| `ByClaimValue` | Same shape as `ByHeaderValue`, for claim mode. |
| `RootPathEntry` / `RootPathAmbiguous` | The single root-path-eligible blueprint, or `null` plus `RootPathAmbiguous = true` when multiple opted in. Preserves the existing `TryResolveByRootPath` behaviour. |
| `All` | Materialised list for the no-match diagnostic log (capped at the resolver's `NoMatchLogCandidateCap`). |

**Invariants**:
- All `FrozenDictionary` instances use case-insensitive ordinal comparers (`StringComparer.OrdinalIgnoreCase`) to preserve the existing case-insensitive matching in `WebRoutingShellResolver`.
- A single `ShellRouteEntry` may appear in multiple dictionaries (e.g., a blueprint with both `Path` and `Host` configured contributes to `ByPathSegment` AND `ByHost`).
- When `RootPathAmbiguous = true`, `RootPathEntry` is `null`. The two fields together encode three states: (no claimant) / (one claimant) / (ambiguous).

---

### `ShellRouteIndexUnavailableException`

Raised by `IShellRouteIndex.TryMatchAsync` when the index is in a degraded state (the very first population attempt failed and there is no usable snapshot). Existing index errors during incremental updates do NOT raise this exception — they leave the previous snapshot in place per FR-012.

```csharp
namespace CShells.AspNetCore.Routing;

public sealed class ShellRouteIndexUnavailableException : Exception
{
    public ShellRouteIndexUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

`WebRoutingShellResolver` catches this exception, logs at `Warning`, returns `null`. The middleware then falls through to the next strategy or to a clean 404 — never a 500-class leakage.

## Relationships

```
IShellBlueprintProvider ──ListAsync──▶ IShellBlueprint.Properties["WebRouting"]
                                              │
                                              ▼
                                   ShellRouteIndexBuilder
                                              │
                                              ▼
                                       ShellRouteEntry[]
                                              │
                                              ▼
                                   ShellRouteIndexSnapshot
                                              │
                  ┌───────────────────────────┴───────────────────────────┐
                  ▼                                                       ▼
       Volatile.Write (publish)                           Volatile.Read (resolver hot path)
                  │                                                       │
                  ▼                                                       ▼
        DefaultShellRouteIndex                                 WebRoutingShellResolver
                  ▲                                                       │
                  │ RefreshAsync                                          │ ResolveAsync
                  │                                                       ▼
       ShellRouteIndexInvalidator                            IShellRouteIndex.TryMatchAsync
                  ▲                                                       │
                  │ INotificationHandler<…>                               ▼
                  │                                              ShellRouteMatch?
            ShellAdded / ShellRemoved / ShellReloaded                     │
                                                                          ▼
                                                          IShellRegistry.GetOrActivateAsync
```

## State transitions

`DefaultShellRouteIndex` has three observable states:

| State | Trigger | Behaviour |
|---|---|---|
| `Uninitialised` | Service constructed; no lookup yet attempted | First `TryMatchAsync` call for a non-name mode (or first call at all if path-by-name is disabled) triggers initial population. Path-by-name lookups complete without populating the snapshot. |
| `Populating` | A refresh is in flight | New `TryMatchAsync` calls return immediately based on the current snapshot (or path-by-name), or wait briefly if the index is in initial population. Concurrent refresh requests coalesce into a single snapshot rebuild via the index's serialization semaphore. |
| `Populated` | A snapshot has been published | All `TryMatchAsync` calls complete sync against `Volatile.Read(snapshot)`. |

State transitions are monotonic in the sense that `Uninitialised → Populated` is a one-way trip; subsequent refreshes replace one `Populated` snapshot with another, never regress to `Uninitialised`. A failed refresh leaves the previous snapshot in place.
