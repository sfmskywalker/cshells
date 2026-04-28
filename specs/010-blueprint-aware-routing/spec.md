# Feature Specification: Blueprint-Aware Path Routing

**Feature Branch**: `010-blueprint-aware-routing`
**Created**: 2026-04-28
**Status**: Draft
**Input**: User description: "Make path-based shell routing discover inactive shells from the blueprint provider so the first request to a known blueprint auto-activates that shell. Removes the de facto requirement to call `PreWarmShells` just to make path routing work, and fixes the post-reload 404 where new generations are not lazily activated by routing."

## Overview

Feature `007` introduced a lazy activation model: the registry holds only *active* shell generations, and a blueprint is materialised on first call to `IShellRegistry.GetOrActivateAsync(name)`. Startup cost became O(pre-warmed shells) instead of O(catalogue size), enabling 100k-tenant deployments.

What was missed in `007`'s design is that `WebRoutingShellResolver` (the built-in HTTP shell-resolution strategy) inspects only `IShellRegistry.GetActiveShells()` to match an incoming request's path / host / header / claim against shell routing configuration. The resolver therefore cannot see any blueprint that has not already been activated — and lazy activation never happens for an unknown shell because the resolver returns `null`, the request never reaches `GetOrActivateAsync`, and ASP.NET Core falls through to a 404. The hot path is a chicken-and-egg: a request needs an active shell to resolve, and an active shell needs a request to be activated.

The currently shipped workaround is to call `.PreWarmShells("Default")` in startup so the registry is non-empty before the first request arrives. This restores routing for the *first* generation, but breaks again immediately after `ReloadAsync` — the new generation is built but not auto-activated, the resolver again sees an empty active-shells list, and every subsequent request returns 404 with no log output. The "lazy activation on first request" guarantee from `007`'s design intent is therefore not currently realised by any built-in routing strategy.

This feature closes the loop by giving the routing layer a read-only, blueprint-backed **shell route index** that maps routing metadata (path, host, header, claim) to a blueprint name *without requiring the shell to be active*. `WebRoutingShellResolver` consults this index instead of the active-shells list, returns the matched name, and `ShellMiddleware`'s existing `GetOrActivateAsync` call then materialises the shell on demand — exactly what the `007` lazy-activation model promises.

`PreWarmShells` is preserved as a performance hint (avoid first-request latency, surface configuration errors at boot) but ceases to be a correctness requirement. The behaviour change is observable as: an out-of-the-box CShells host with one or more configured shells routes correctly without any explicit `.PreWarmShells(...)` call.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — First request to a cold blueprint auto-activates and serves (Priority: P1)

A host registers a blueprint provider with two shells (`Default` at path `""`, `acme` at path `"acme"`). The host does NOT call `.PreWarmShells(...)`. At startup the registry holds zero active shells. When the first HTTP request `GET /elsa/api/identity/login` arrives, `WebRoutingShellResolver` consults the route index, matches the request path's first segment / root-path rule against the `Default` blueprint, returns `ShellId("Default")`, and `ShellMiddleware` calls `GetOrActivateAsync("Default")` which materialises generation `Default#1`, registers its endpoints, and forwards the request. The response is `200 OK`, not `404`.

**Why this priority**: This is the central correctness issue. Without it, `007`'s "lazy activation on first request" is not actually delivered for the only built-in routing strategy CShells ships, and every consumer is silently forced to use `.PreWarmShells(...)` to avoid 404s.

**Independent Test**: Configure two shells via a stub provider (`Default` at path `""`, `acme` at path `"acme"`); start the host without calling `.PreWarmShells(...)`; assert `_registry.GetActiveShells()` is empty; issue `GET /` and assert it activates `Default` and returns `200`; issue `GET /acme/x` and assert it activates `acme` and returns `200`; assert each provider lookup happens exactly once per shell name.

**Acceptance Scenarios**:

1. **Given** a provider with shells `Default` (path `""`) and `acme` (path `"acme"`), and no `.PreWarmShells(...)` call, **When** `GET /something` arrives, **Then** the resolver returns `ShellId("Default")`, the registry activates `Default#1`, endpoints register, and the response is served by the `Default` shell.
2. **Given** the same setup, **When** `GET /acme/x` arrives, **Then** the resolver returns `ShellId("acme")`, the registry activates `acme#1`, and the request is scoped to the `acme` shell.
3. **Given** the same setup, **When** a request arrives for an unknown path segment that does not match any blueprint's `WebRouting:*` configuration, **Then** the resolver returns `null`, the next strategy in the resolver pipeline runs, and absent any match the request falls through to non-shell middleware as today.
4. **Given** the provider's `GetAsync(name)` throws because the underlying source is unavailable, **When** the resolver matches a name and the middleware calls `GetOrActivateAsync`, **Then** the caller sees `503` (existing `ShellBlueprintUnavailableException` translation), and the route index does not cache a poison entry — a subsequent request retries.

---

### User Story 2 — A reloaded shell is auto-activated by the next matching request (Priority: P1)

An operator hits `POST /elsa/api/shells/reload`. The registry drains generation `Default#1` (`Active → Deactivating → Disposed`), `ShellEndpointRegistrationHandler` removes its endpoints, and the registry holds zero active shells again. The next HTTP request `POST /elsa/api/identity/login` arrives: the resolver consults the route index (which still knows about the `Default` blueprint because the blueprint itself was not removed), returns `ShellId("Default")`, and `ShellMiddleware`'s `GetOrActivateAsync` materialises generation `Default#2`. The request succeeds. Today this returns `404` with no logs.

**Why this priority**: Without this, the reload feature shipped in `009-management-api` is unusable: the very next request after reload silently 404s, and any production deployment that calls reload will appear to have lost all shell routing until the host is restarted. The current `.PreWarmShells(...)` workaround does not help — it only pre-warms at host startup, not after each reload.

**Independent Test**: Pre-warm `Default` (or rely on User Story 1's lazy activation — both must work); confirm a request returns `200`; call `POST /shells/reload`; assert the registry holds zero active shells immediately after the reload completes; issue another request and assert it activates `Default#2` and returns `200`.

**Acceptance Scenarios**:

1. **Given** an active shell `Default#1` serving requests, **When** the operator successfully reloads `Default`, **Then** generation `Default#1` is drained and the next request to a `Default`-matching path activates `Default#2` and is served.
2. **Given** a reload completes and `Default#2` is active, **When** the operator reloads `Default` again, **Then** the same lazy re-activation occurs for `Default#3`, and the cycle is repeatable.
3. **Given** a host with multiple shells `[Default, acme, contoso]`, **When** the operator reloads `acme` only, **Then** subsequent requests to `Default` and `contoso` continue to be served by their existing generations without re-activation, and only requests routed to `acme` trigger an activation of `acme#N+1`.

---

### User Story 3 — `PreWarmShells` remains an optional performance hint (Priority: P2)

A host with a single critical shell that is expensive to build (large feature graph, slow first-time configuration binding) wants to absorb the build cost at startup rather than paying it on the first user-visible request. The host calls `.PreWarmShells("Default")`. Startup logs show `pre-warming 1 shell(s)`, the shell transitions to `Active` before the host opens the listening socket, and the first request observes warm-cache latency. After this feature, `.PreWarmShells(...)` produces the same observable behaviour as today, except that omitting the call no longer causes `404`s.

**Why this priority**: Pre-warming is still a useful tool for latency-sensitive hosts and CI smoke tests; it must continue to work unchanged. This story exists to make that explicit and prevent regressions where someone "simplifies" pre-warming to a no-op once the route index lands.

**Independent Test**: Configure two shells; call `.PreWarmShells("Default")`; assert that immediately after `app.RunAsync()` returns, `_registry.GetActiveShells()` contains `Default#1` but not `acme#1`; assert that the first request to `acme` still activates `acme#1` lazily as in User Story 1.

**Acceptance Scenarios**:

1. **Given** a host that calls `.PreWarmShells("Default", "acme")`, **When** the host finishes startup, **Then** both shells are `Active` before the first HTTP request and the first request to either shell observes no activation latency.
2. **Given** a host that does not call `.PreWarmShells(...)`, **When** the host finishes startup, **Then** zero shells are `Active`, the startup log records "pre-warming 0 shell(s)" (or equivalent informational language; not "registry remains idle until first activation" implying lazy is broken), and lazy activation works per User Story 1.
3. **Given** a host that calls `.PreWarmShells("nonexistent")`, **When** the host starts, **Then** the failure is logged at warning level (matching today's behaviour), startup completes, and lazy activation continues to work for blueprints that do exist.

---

### User Story 4 — Route index discovery scales with the configured catalogue, not with the on-demand catalogue (Priority: P2)

A host registers a blueprint provider claiming 100,000 shell blueprints. The host wants the route index to support path/host/header/claim matching against any of those blueprints without enumerating the full catalogue at host startup or on every request. The route index MUST therefore consult the provider on demand for catalogue entries, MAY cache routing metadata for blueprints it has already seen, and MUST NOT block startup waiting for catalogue enumeration to complete.

**Why this priority**: The 100k-tenant scaling target was the justification for `007`'s lazy model. A naïve route index that calls `provider.ListAsync()` exhaustively at startup or on every request would re-introduce the cost `007` removed. The route index is required to be implementable in a way that preserves the SC-001-style scaling promise.

**Independent Test**: Configure a stub provider whose `ListAsync` is instrumented to throw if called more than once with `Cursor = null`; start the host with no pre-warm; assert startup completes; issue a single request and assert it matches and activates a shell; assert that `ListAsync` was either never called (lookup-by-name path used) or called exactly with bounded paging consistent with the implementation strategy chosen in `plan.md`.

**Acceptance Scenarios**:

1. **Given** a provider claiming 100,000 blueprints, **When** the host starts without pre-warm, **Then** startup time is bounded by O(pre-warmed shells) (per the existing `007` SC-001 metric) and the route index does not exhaustively enumerate the catalogue at startup.
2. **Given** a request whose first path segment is a known short-name (e.g., the URL path segment IS the blueprint name, as is the convention for `Path` mode), **When** the resolver runs, **Then** the route index resolves with at most one provider call (`GetAsync(name)` or `ExistsAsync(name)`), not a full enumeration.
3. **Given** a request whose routing metadata cannot be derived from the URL alone (e.g., a host like `acme.example.com` where `acme` may or may not be the blueprint name, or a header value that may map to a blueprint via `WebRouting:Host`/`WebRouting:HeaderName`), **When** the resolver runs, **Then** the route index either (a) consults a host-side cache that was populated incrementally as blueprints became known to the system, or (b) fails the match cleanly and lets the next resolver strategy run — it does NOT call `provider.ListAsync()` synchronously on the request hot path.

---

### User Story 5 — Operator can diagnose route-resolution outcomes (Priority: P3)

An operator hits a 404 they did not expect and needs to know why. With pre-warm-only routing today, the failure is silent: zero log lines fire because the resolver never enters its match logic for a cold registry. After this feature, the resolver MUST emit a single structured log entry per request that fails to resolve, identifying the routing metadata it considered (path / host / header / claim values), and the path-routing-enabled blueprints it knew about (capped by a configurable count to avoid log spam in 100k-tenant deployments).

**Why this priority**: Silent 404s with no log output is the exact failure mode that consumed multiple hours of debugging in the original report. A first-class diagnostic log entry is cheap and prevents re-occurrence.

**Independent Test**: Configure a single shell at path `acme`; issue `GET /unknown/x`; assert that the resolver emitted a single `Information`-level log entry (or `Debug` if Information is too noisy in production — left to plan.md) containing the requested path, the routing modes the resolver attempted, and a representation of the candidate blueprints (e.g., "Path=acme") that did NOT match.

**Acceptance Scenarios**:

1. **Given** an unmatched request, **When** the resolver fails to find a shell, **Then** a single log entry is emitted that names the rejected request path / host / header values and a bounded representation of the blueprints the resolver considered.
2. **Given** the host's catalogue has 100,000 blueprints, **When** the same unmatched request arrives, **Then** the log entry lists at most N candidate blueprints (N is configurable; default in the order of 10) and indicates the truncation, rather than serialising the whole catalogue.
3. **Given** the route index encountered an internal error (e.g., the provider threw a transient exception while populating the index), **When** the resolver attempts a match, **Then** the failure is logged at warning level with the inner exception, the resolver returns `null` (the request gets a clean 404 from non-shell middleware rather than a 500), and a subsequent request retries the index population.

---

### Edge Cases

- **Multiple blueprints share the same `WebRouting:Path` value (e.g., two shells claim path `acme`).** The route index MUST detect this conflict and either (a) reject the duplicate at index-population time with a warning and exclude the later-encountered blueprint from path routing, or (b) return `null` for the ambiguous match (matching the behaviour of `TryResolveByRootPath` for ambiguous root claimants). The exact policy is locked down in `plan.md`; the spec requires that the system never silently routes to one of two equally-eligible shells.
- **A blueprint declares no `WebRouting:Path`/`Host`/`HeaderName`/`ClaimKey`.** It is not eligible for the corresponding routing mode. The resolver must skip it cleanly without throwing.
- **A blueprint is added at runtime via the management API.** The route index MUST become aware of the new blueprint without requiring a host restart. The mechanism (lifecycle event subscription, on-write notification from the provider, or lazy lookup-on-miss) is a `plan.md` decision; the spec only requires that a blueprint added after host startup is reachable by the next matching request.
- **A blueprint is removed at runtime via `UnregisterBlueprintAsync`.** The route index MUST stop returning that blueprint within a bounded number of requests. A request matching a freshly removed blueprint MAY return either `404` or be served by a still-draining generation; the spec requires only that no new shell scopes are opened for a removed blueprint.
- **A blueprint's routing configuration is changed via reload (`Path` value updated).** The route index entries for that blueprint MUST refresh on reload completion. The next matching request uses the new configuration.
- **`WebRouting:Path` starts with `/`.** Today the resolver throws `InvalidOperationException` mid-request (`WebRoutingShellResolver.cs:93`). The route index SHOULD detect this at index-population time and surface the misconfiguration at startup (or on first encounter), not on the hot path.
- **Two shells both have `WebRouting:Path = ""` (root-path opt-in).** Today the resolver returns `null` (ambiguous) and the next strategy decides. The route index MUST preserve this behaviour.
- **The host registers a custom `IShellResolverStrategy` that does not consult the route index.** Custom strategies remain free to implement their own resolution; the spec only constrains the built-in `WebRoutingShellResolver`. The route index MUST be available as a public service so custom strategies can opt into the same blueprint-aware lookup if they want it.
- **`EnablePathRouting = false`.** The route index need not be populated for path-routing entries; `WebRoutingShellResolver.TryResolveByPath` short-circuits to `null` as today.

## Current Architecture Interaction Analysis

| Component | Current role in CShells | Required behaviour direction for this feature |
| --- | --- | --- |
| `WebRoutingShellResolver` | Iterates `IShellRegistry.GetActiveShells()` and matches each shell's `WebRouting:*` configuration against the request's path / host / header / claim. Returns `null` if no match. | Replace the active-shells iteration with a query against a new shell route index that knows about *all* configured blueprints, not just active ones. The resolver returns the matched blueprint name and lets `ShellMiddleware` activate it lazily via the existing `GetOrActivateAsync` call. |
| `ShellMiddleware` | Calls `_resolver.Resolve(...)`, then `_registry.GetOrActivateAsync(shellId.Value.Name, ...)`. Translates `ShellBlueprintNotFoundException` → 404 and `ShellBlueprintUnavailableException` → 503. | No change. Today this code is unreachable for cold blueprints because the resolver returns `null`; after this feature it becomes the lazy-activation hot path that `007` always intended. |
| `IShellRegistry` | Holds active generations, lazily activates on `GetOrActivateAsync`. Publishes lifecycle notifications. | No change to the contract. The route index is a *consumer* of the registry's lifecycle notifications (specifically blueprint add/remove/reload) for invalidation, not a co-owner of registry state. |
| `IShellBlueprintProvider` | Provides `GetAsync(name)`, `ExistsAsync(name)`, `ListAsync(query)`. | No contract change required. The route index uses `GetAsync` for known-name lookups and MAY use `ListAsync` for incremental population (decision deferred to `plan.md`). The hot-path lookup remains O(1) per `GetAsync` contract. |
| `CShellsStartupHostedService` | If a pre-warm list is configured, activates each named shell in parallel. Otherwise logs "registry remains idle until first activation" and returns. | No behavioural change to pre-warm. The misleading log line "registry remains idle until first activation" is rephrased to reflect the new reality (the registry IS idle, but routing now activates shells lazily as designed). |
| `CShellsBuilder.PreWarmShells` | Adds shell names to a pre-warm list consumed by `CShellsStartupHostedService`. | No contract change. Documentation is updated to describe pre-warming as a latency-optimisation hint, not a correctness requirement for routing. |
| `ShellEndpointRegistrationHandler` | Subscribes to `ShellActivated`/`ShellDeactivating` lifecycle notifications and adds/removes endpoints in `DynamicShellEndpointDataSource`. | No change. Endpoints continue to register on `ShellActivated` and remove on `ShellDeactivating`, which now fires correctly during lazy activation triggered by `WebRoutingShellResolver` + `ShellMiddleware`. |
| `IShellLifecycleSubscriber` (the lifecycle-notification mechanism) | Carries `ShellActivated`, `ShellDeactivating`, `ShellAdded`, `ShellRemoved`, `ShellReloaded` notifications. | The route index subscribes to `ShellAdded`, `ShellRemoved`, and `ShellReloaded` (not `ShellActivated`/`Deactivating`) for cache invalidation, since route metadata is a property of the blueprint, not of the active generation. |

## Requirements *(mandatory)*

### Functional Requirements

**Shell route index — public surface**

- **FR-001**: The system MUST expose a new abstraction (working name `IShellRouteIndex`, final name pinned in `plan.md`) representing a read-only mapping from request-side routing identifiers (path segment, host, header value, claim value) to a blueprint name. The index lives in `CShells.AspNetCore.Abstractions`.
- **FR-002**: The route index MUST expose a single primary operation conceptually equivalent to `Task<ShellId?> TryMatchAsync(ShellRouteCriteria criteria, CancellationToken ct)` where `ShellRouteCriteria` carries the routing values extracted from the request (Path first segment, Host, Header values, Claim values) plus a routing-mode discriminator. The exact API shape is pinned in `contracts/IShellRouteIndex.md`.
- **FR-003**: The route index MUST be safe to call concurrently from request threads.
- **FR-004**: The route index MUST be registered as a singleton in DI by default.

**Resolver behaviour change**

- **FR-005**: `WebRoutingShellResolver` MUST consult the route index for path / host / header / claim matching instead of iterating `IShellRegistry.GetActiveShells()`.
- **FR-006**: `WebRoutingShellResolver` MUST preserve its existing public observable behaviour for inputs that today produce a non-`null` result, with one explicit exception: blueprints that today are invisible because they are not yet active will become visible to the resolver and produce non-`null` results.
- **FR-007**: `WebRoutingShellResolver` MUST preserve its existing edge-case handling: paths starting with `/` raise the existing `InvalidOperationException` (or, per Edge Case in §Edge Cases, surface it earlier at index-population time); ambiguous root-path claims (multiple shells with `WebRouting:Path = ""`) return `null` so the next strategy decides.
- **FR-008**: The resolver MUST emit a single structured log entry per request when no match is found, including the routing metadata it considered and a bounded representation of the candidate blueprints (cap configurable; default in the order of 10).
- **FR-009**: The resolver MUST NOT call `IShellBlueprintProvider.ListAsync` directly. All provider interaction is mediated by the route index.

**Index invalidation and lifecycle**

- **FR-010**: The route index MUST refresh entries when a blueprint is added (`ShellAdded` notification), removed (`ShellRemoved`), or reloaded (`ShellReloaded`).
- **FR-011**: The route index MUST NOT refresh on `ShellActivated` or `ShellDeactivating` notifications — routing metadata is a property of the blueprint, not the active generation.
- **FR-012**: The route index MUST tolerate provider exceptions during refresh: an in-progress refresh that throws MUST NOT corrupt the previously-good index, MUST be logged at warning level, and MUST be retried on the next invalidation event.
- **FR-013**: When the route index is being refreshed, concurrent reads MUST observe either the previous fully-consistent state or the new fully-consistent state — never a partial state.

**Pre-warm preservation**

- **FR-014**: `CShellsBuilder.PreWarmShells(params string[])` MUST continue to work as today: shells listed are activated in parallel during `CShellsStartupHostedService.StartAsync` and become `Active` before the host opens its listening socket.
- **FR-015**: Calling `.PreWarmShells(...)` with names that do not exist in the catalogue MUST log at warning level and MUST NOT block other named shells from pre-warming.
- **FR-016**: A host that does NOT call `.PreWarmShells(...)` MUST be able to serve every request whose routing metadata matches a configured blueprint, with the only observable cost being the first-request activation latency for that blueprint.

**Diagnostics**

- **FR-017**: `CShellsStartupHostedService` MUST replace the misleading log line "registry remains idle until first activation" with language that reflects the new reality (the registry is idle by design and is materialised lazily by routing).

**Compatibility — what does NOT change**

- **FR-018**: `IShellRegistry`, `IShellBlueprintProvider`, `IShellBlueprintManager`, `IShellLifecycleSubscriber`, `ShellEndpointRegistrationHandler`, `ShellMiddleware` public surfaces are unchanged.
- **FR-019**: Custom `IShellResolverStrategy` implementations are unaffected: they may continue to operate against `IShellRegistry.GetActiveShells()` as today, or opt into `IShellRouteIndex` for blueprint-aware resolution.
- **FR-020**: The configuration shape for shells (`Configuration.WebRouting.{Path,Host,HeaderName,ClaimKey}`) is unchanged.
- **FR-021**: The `WebRoutingShellResolverOptions` configuration shape (`EnablePathRouting`, `EnableHostRouting`, `HeaderName`, `ClaimKey`, `ExcludePaths`) is unchanged.

### Key Entities

- **`IShellRouteIndex`**: The new abstraction. Read-only, blueprint-backed, asynchronous-safe mapping from `ShellRouteCriteria` to `ShellId?`. Subscribes to lifecycle notifications for invalidation. Exposed as a singleton.
- **`ShellRouteCriteria`**: Value object carrying the routing metadata extracted from the current request (path's first segment, host, header values, claim values, routing-mode flags). Constructed by `WebRoutingShellResolver` from `ShellResolutionContext` and passed to the index. Final shape pinned in `contracts/`.
- **`ShellRouteEntry`**: Internal record describing a single blueprint's routing configuration (blueprint name, path, host, header value, claim value). Populated from `IShellBlueprint.Properties["WebRouting:*"]` (or equivalent — concrete property accessor in `data-model.md`).
- **`IShellRouteIndexBuilder`** *(internal)*: Contract used by the index implementation to materialise `ShellRouteEntry` records from the provider, with policy hooks for duplicate detection and malformed-config handling.

### Assumptions

- The 100k-tenant scaling target (`007` SC-001) remains a constraint. The route index implementation MAY rely on incremental on-demand population (e.g., `provider.GetAsync(name)` for path-mode where the URL segment is the name) rather than eager `ListAsync` enumeration. The exact strategy is in `plan.md`/`research.md`.
- The path-routing convention "first URL path segment IS the blueprint name" continues to hold for `WebRouting:Path` mode where the path equals the blueprint's short name. (Path mode where multiple segments map to a shell is out of scope for this feature.)
- For host / header / claim modes, where the routing identifier may differ from the blueprint name, the route index MAY require eager catalogue listing OR a per-blueprint hint mechanism (e.g., the provider returns the routing metadata alongside the blueprint). Decision deferred to `plan.md`.
- The host-startup race observed in the originating bug report (Nuplane finishing package loading after CShells startup hosted service ran) is OUT OF SCOPE for this feature. That race is a property of host composition, not of CShells. After this feature, the race becomes harmless (lazy activation works even if the catalogue is fully populated only after listening starts) — which is itself a desirable side-effect.
- The downstream consumer `Elsa.ModularServer.Web` removes its `.PreWarmShells("Default")` call when adopting the CShells version that ships this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host configured with one shell (`Default`, `WebRouting:Path = ""`) and **no** `.PreWarmShells(...)` call MUST serve `GET /any/path` with `200 OK` (or the application's response) on the first request after `app.RunAsync()` returns. Today this returns `404`.
- **SC-002**: After `POST /shells/reload` completes for a previously-active shell, the immediately-following request to that shell MUST be served (activating the new generation lazily). Today this returns `404`.
- **SC-003**: Host startup time for a host with one configured shell and no pre-warm MUST remain within ±50 ms of host startup time without this feature (i.e., the route index does not add startup cost beyond noise).
- **SC-004**: Host startup time for a host with a provider claiming 100,000 blueprints MUST remain within ±50 ms of host startup time with 10 blueprints (the `007` SC-001 metric is preserved). The route index does not exhaustively enumerate the catalogue at startup.
- **SC-005**: A request that fails to resolve to any shell MUST emit exactly one log entry containing the rejected routing metadata and a bounded representation of the candidate blueprints. Today such requests produce zero log output.
- **SC-006**: Every existing feature `005`/`006`/`007`/`008`/`009` test scenario continues to pass after this feature, demonstrating that lifecycle, drain, blueprint-provider, and management-API behaviours are not regressed.
- **SC-007**: The Workbench sample (`samples/CShells.Workbench`) MUST work end-to-end without any explicit `.PreWarmShells(...)` call in its `Program.cs`. (If the Workbench currently calls `PreWarmShells`, the call is removed in this feature's implementation.)
- **SC-008**: The downstream consumer `Elsa.ModularServer.Web` (out-of-tree) MUST work without `.PreWarmShells("Default")` once it picks up the CShells version that ships this feature. This is a manual smoke-check, not an automated test in this repo.
