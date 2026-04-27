# Implementation Plan: Shell Management REST API

**Branch**: `009-management-api` | **Date**: 2026-04-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/009-management-api/spec.md`

## Summary

Ship a new optional NuGet package `CShells.Management.Api` that maps a small
set of root-level Minimal API endpoints onto an existing
`IEndpointRouteBuilder`. Hosts install the endpoints with one line —
`app.MapShellManagementApi("/_admin/shells")` — and chain their own
authorization, CORS, rate-limiting, OpenAPI, and endpoint-filter conventions
on the returned `RouteGroupBuilder`.

The endpoint surface (six routes — list, focused-view, blueprint,
reload-one, reload-all, force-drain) is a thin HTTP veneer over
`IShellRegistry`. The package depends only on `CShells.Abstractions` plus
the `Microsoft.AspNetCore.App` framework reference; it deliberately does
**not** reference `CShells.AspNetCore`, since management endpoints are
inherently cross-shell (they call the root-scoped registry) and feature
authors of `IWebShellFeature` shouldn't have to pull in the management
surface.

To make per-generation drain observability and the force-drain endpoint
first-class (not workarounds), this feature also lands a small abstraction
extension in `CShells.Abstractions`: `IShell.Drain` exposes the in-flight
`IDrainOperation` for any shell whose lifecycle state is `Deactivating`,
`Draining`, or `Drained`. The `Shell` implementation already has the
information (the registry currently keeps it in a private
`ConcurrentDictionary<IShell, Lazy<DrainOperation>>`); this feature moves
the reference onto the shell and uses it for `DrainAsync` idempotency,
deleting the dictionary.

The Workbench sample wires `app.MapShellManagementApi("/_admin/shells")`
unprotected, with an explicit code comment flagging it sample-only, and
documents `curl` usage in its README.

## Technical Context

**Language/Version**: C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for the new library project; tests target `net10.0`
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.AspNetCore.App` framework reference (Minimal APIs, problem-details, System.Text.Json), `Ardalis.GuardClauses`. **No new third-party dependencies.** Specifically: no `CShells.AspNetCore`, no FastEndpoints, no Newtonsoft.Json.
**Storage**: N/A — endpoints are a thin HTTP layer over the in-memory registry.
**Testing**: xUnit 2.x with `Assert.*`. Integration tests use `WebApplicationFactory<TEntryPoint>` against a test host. The existing test fixture pattern (`DefaultShellHostFixture` etc.) covers most needs; new `ManagementApiFixture` builds a minimal `WebApplication` with a small set of named shells and the management endpoints mapped under `/admin`.
**Target Platform**: .NET server / generic web host. Runs anywhere the host's ASP.NET Core stack runs.
**Project Type**: Optional library that contributes ASP.NET Core endpoints. No CLI, no UI, no service registration — install method only.
**Performance Goals**: Manual-testing tool; no specific throughput target. Each endpoint is O(in-memory registry lookup) plus JSON serialization. Force-drain awaits each in-flight drain to terminal state, so its response time is bounded by the longest grace-period across the shell's draining generations (per Q3 clarification).
**Constraints**: (a) The package MUST NOT register any service in DI — `app.MapShellManagementApi(...)` is the entire integration surface. (b) Returns `RouteGroupBuilder` so consumers chain standard endpoint conventions without package-specific glue (FR-014 / SC-006). (c) FR-004's `IShell.Drain` extension MUST surface the same instance the registry already returns from `DrainAsync`/`ReloadAsync` — no new tracker, snapshot, or proxy.
**Scale/Scope**: Up to a few thousand active shells per host (consistent with existing CShells scale targets from `007`/`008`). The `GET /` list endpoint paginates via the existing `ShellListQuery` cursor model so large catalogues page correctly.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Abstraction-First** | ✅ PASS | The `IShell.Drain` extension lands in `CShells.Abstractions/Lifecycle/IShell.cs`. The new `MapShellManagementApi` extension method is implementation, not an extensibility seam (it returns the standard ASP.NET Core `RouteGroupBuilder`, not a custom builder). Internal DTOs in `CShells.Management.Api` are `internal sealed record` — they're response shapes, not consumer contracts, so they belong in the implementation project. |
| **II. Feature Modularity** | ✅ PASS | The management API is **not** an `IWebShellFeature` — it's a root-level endpoint group, by design. Per-shell feature model is untouched. The package itself is feature-modular (host opts in via the install method); no impact on existing feature discovery. |
| **III. Modern C# Style** | ✅ PASS | New project enables nullable, implicit usings, file-scoped namespaces, primary constructors, expression-bodied members, collection expressions. `internal sealed` on every implementation type. Public surface is one extension method + the new `IShell.Drain` property. |
| **IV. Explicit Error Handling** | ✅ PASS | Endpoint handlers translate registry exceptions to HTTP statuses per FR-013's table using `Results.Problem(...)` (RFC 7807). Guard clauses (`Guard.Against.Null`, `Guard.Against.NullOrWhiteSpace`) on the install method's parameters. Per-shell failures inside batch reload are surfaced in the per-entry `error` field, not silently swallowed. |
| **V. Test Coverage** | ✅ PASS | New test folder `tests/CShells.Tests/Integration/Management/` with `WebApplicationFactory`-based tests covering each route + the error-mapping table + the parallelism validation + the in-flight-drain observation + the authorization-passthrough scenario. New unit tests cover the `IShell.Drain` lifecycle invariants (FR-004) and the DTO mapping. Targeting +12 tests minimum (SC-008). |
| **VI. Simplicity & Minimalism** | ✅ PASS | The package is the minimum viable HTTP veneer over the existing registry — six routes, no service registrations, no options class, no opinion about auth. The `IShell.Drain` extension is a one-property addition that **simplifies** the registry by eliminating the existing `_drainOps` dictionary (the drain reference moves onto the Shell where it always belonged). |
| **VII. Lifecycle & Concurrency** | ✅ PASS | `IShell.Drain`'s setter is a CAS-based publish-once on the `Shell` instance (per the existing `_state` / `_disposeTask` pattern). Concurrent `DrainAsync` calls observe the same drain instance via the published reference, preserving the current "same instance for concurrent calls" contract. Force-drain endpoint walks `IShellRegistry.GetAll(name)` (already thread-safe) and awaits each in parallel; the registry's per-name `SemaphoreSlim` is untouched. |

**No constitution violations.** This feature adds one library project, one
abstraction property, and a small number of HTTP handlers. No complexity
budget claims required.

## Project Structure

### Documentation (this feature)

```text
specs/009-management-api/
├── plan.md                                ← this file
├── research.md                            ← Phase 0
├── data-model.md                          ← Phase 1
├── quickstart.md                          ← Phase 1
├── contracts/                             ← Phase 1
│   ├── IShell.md                            (delta: new Drain property)
│   └── ManagementApi.md                     (HTTP endpoints contract)
├── checklists/
│   └── requirements.md                    ← /speckit.specify output (resolved)
└── tasks.md                               ← Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/CShells.Abstractions/
└── Lifecycle/
    └── IShell.cs                          (MODIFIED — add `IDrainOperation? Drain { get; }` property)

src/CShells/
└── Lifecycle/
    ├── Shell.cs                           (MODIFIED — back the Drain property; CAS-publish setter)
    └── ShellRegistry.cs                   (MODIFIED — DrainAsync uses Shell.Drain for idempotency; remove _drainOps dictionary)

src/CShells.Management.Api/                (NEW PROJECT)
├── CShells.Management.Api.csproj          (multi-target net8.0;net9.0;net10.0; references CShells.Abstractions; FrameworkReference Microsoft.AspNetCore.App)
├── README.md                              (NuGet package readme)
├── EndpointRouteBuilderExtensions.cs      (public — `MapShellManagementApi(this IEndpointRouteBuilder, string prefix = "/_admin/shells")`)
├── Endpoints/
│   ├── ListShellsHandler.cs               (internal — GET / handler)
│   ├── GetShellHandler.cs                 (internal — GET /{name})
│   ├── GetBlueprintHandler.cs             (internal — GET /{name}/blueprint)
│   ├── ReloadShellHandler.cs              (internal — POST /reload/{name})
│   ├── ReloadAllHandler.cs                (internal — POST /reload-all)
│   ├── ForceDrainHandler.cs               (internal — POST /{name}/force-drain)
│   └── ResultMapper.cs                    (internal — exception → ProblemDetails translation)
└── Models/
    ├── ShellPageResponse.cs               (internal sealed record)
    ├── ShellListItem.cs                   (internal sealed record)
    ├── ShellDetailResponse.cs             (internal sealed record)
    ├── ShellGenerationResponse.cs         (internal sealed record)
    ├── BlueprintResponse.cs               (internal sealed record — includes ConfigurationData verbatim per FR-012a)
    ├── ReloadResultResponse.cs            (internal sealed record)
    ├── DrainSnapshot.cs                   (internal sealed record — status + deadline)
    └── DrainResultResponse.cs             (internal sealed record — per-handler results)

tests/CShells.Tests/
├── Unit/Lifecycle/
│   └── ShellDrainPropertyTests.cs         (NEW — FR-004 invariants: null pre-drain, set during Deactivating/Draining/Drained, same instance as DrainAsync return)
└── Integration/Management/                (NEW)
    ├── ManagementApiFixture.cs            (WebApplicationFactory-based; minimal host with N named shells)
    ├── ListShellsEndpointTests.cs         (US3.1, paging, empty registry edge case)
    ├── GetShellEndpointTests.cs           (US3.2/3.4/3.5, in-flight drain observation, 404)
    ├── GetBlueprintEndpointTests.cs       (US3.6, no-side-effect activation, 404 on unknown name)
    ├── ReloadShellEndpointTests.cs        (US1, error mapping for not-found / unavailable / shutdown)
    ├── ReloadAllEndpointTests.cs          (US2, US6, parallelism query validation, partial failure)
    ├── ForceDrainEndpointTests.cs         (US4, including the two-draining-generation scenario, no-in-flight 404)
    └── AuthorizationPassthroughTests.cs   (US5, RequireAuthorization + AddEndpointFilter chains)

samples/CShells.Workbench/
├── CShells.Workbench.csproj               (MODIFIED — add ProjectReference to CShells.Management.Api)
├── Program.cs                             (MODIFIED — add `app.MapShellManagementApi("/_admin/shells");` with sample-only comment; remove the existing inline `MapGet("/_shells/status", ...)` ad-hoc admin endpoint since the new GET / supersedes it)
└── README.md                              (MODIFIED — add "Manual Testing via the Management API" section per FR-016)

CShells.sln                                (MODIFIED — add CShells.Management.Api project)
.specify/templates/agent-file-template.md  (no change — agent context update happens via update-agent-context.sh)
```

**Structure Decision**: One new library project (`src/CShells.Management.Api/`),
one abstraction-property edit (`IShell.Drain`), one implementation edit
(`Shell` + `ShellRegistry` use the new property for idempotency), one new
integration-test folder, and a small Workbench update. No new directories
in `src/CShells/` or `src/CShells.Abstractions/`. The new project follows
the existing per-project README + framework-reference pattern shipped by
`CShells.AspNetCore`.

## Complexity Tracking

> No constitution violations to justify. The new project is the smallest
> viable HTTP veneer; the abstraction-extension is a one-property addition
> that simplifies the registry (deleting `_drainOps`).

The only design tension worth recording is between principles **I**
(Abstraction-First) and **VI** (Simplicity). Adding `IShell.Drain` to a
dedicated abstractions project is principle-I behavior; doing so to enable
**one consumer's** force-drain endpoint could read as speculative. The
counter-argument: the existing `_drainOps` dictionary in the registry is
**already** an implementation of "drain-by-shell lookup" — surfacing it
on the abstraction is a refactoring that strictly *removes* a private
data structure. The abstraction is justified by an actual current need
(this feature) and simplifies the code; it's not speculative.
