# Phase 0 Research: Shell Management REST API

**Feature**: [009-management-api](spec.md)
**Date**: 2026-04-27

The feature has a small design surface — six HTTP routes, one abstraction
property, one new project. Five open questions; all resolved without any
remaining `NEEDS CLARIFICATION` markers. The user-facing scope (security,
force-drain semantics) was already nailed down in the `/speckit.clarify`
session recorded in `## Clarifications` of `spec.md`.

## R-001: Where does the per-generation drain reference live?

**Decision**: Add `IDrainOperation? Drain { get; }` to `IShell` in
`CShells.Abstractions/Lifecycle/IShell.cs`. The default implementation
(`Shell` in `CShells/Lifecycle/Shell.cs`) backs it with a private nullable
field, published by the registry via an `internal void SetDrain(DrainOperation)`
method using a CAS to ensure publish-once semantics.

**Rationale**:

- The drain reference is intrinsic to a non-active generation: the moment
  `ShellLifecycleState.Deactivating` or `Draining` is set, there is exactly
  one drain operation associated with that generation. Putting the
  reference on the entity that owns the lifecycle state matches the data's
  natural home.
- The current registry tracks this in a private
  `ConcurrentDictionary<IShell, Lazy<DrainOperation>> _drainOps`. That
  dictionary exists *only* to give `DrainAsync` its idempotency guarantee
  ("concurrent callers for the same shell receive the same instance"). If
  the drain reference lives on the shell itself, the dictionary becomes
  redundant and can be deleted (R-002).
- Constitutional principle I (Abstraction-First) requires the property to
  land in `*.Abstractions` since it's a public consumer-extensibility
  contract. The force-drain endpoint **and** any in-process caller will
  read it; surfacing it on `IShell` lets both consumers use the same path.

**Alternatives considered**:

- **Add `IShellRegistry.GetDrainOperation(IShell shell)` instead.** Rejected.
  Would require callers to thread the registry through, even when they
  already hold the `IShell`. Less ergonomic; doesn't simplify the registry.
- **Add `IShellRegistry.GetDrainOperations(string name)` returning the
  collection of in-flight drains for a shell name.** Rejected. The
  management API does call this conceptually, but it's trivially built on
  top of `GetAll(name)` + per-generation `IShell.Drain`. Adding a registry
  method on top of that is redundant API surface.
- **Keep `_drainOps` dictionary; add `IShellRegistry.TryGetDrainOperation`
  that consults it.** Rejected. Adds API surface for what is already
  trackable through the existing `IShell` abstraction once we add the
  property. Constitution principle VI (Simplicity) prefers fewer moving
  parts.

## R-002: Does `IShell.Drain` replace the `_drainOps` dictionary?

**Decision**: Yes. `ShellRegistry.DrainAsync` reads `shell.Drain` first; if
non-null, returns it. Otherwise creates the `DrainOperation`, calls
`shell.SetDrain(op)` (CAS-publish), and starts the run. The
`ConcurrentDictionary<IShell, Lazy<DrainOperation>>` field and the
`Lazy<>`-based GetOrAdd pattern are deleted.

**Rationale**:

- The `Lazy<>` wrapper exists to defeat `ConcurrentDictionary.GetOrAdd`'s
  "factory may run more than once" hazard. Once the drain reference lives
  on the `Shell` instance and is published via a single CAS, that hazard
  doesn't exist — `Interlocked.CompareExchange` is the canonical
  publish-once primitive.
- The dictionary's other purpose was memory hygiene: it removed entries
  when the run completed so long-lived hosts didn't accumulate one
  drained-Shell + DrainOperation + TCS + CTS per reload. Moving the
  reference onto `Shell` gives the same garbage-collection profile: when
  the registry releases the slot's reference to the drained `Shell`, both
  the `Shell` and its `DrainOperation` become eligible for GC together.
  No accumulation.
- Concurrency contract preserved: per the existing
  `IDrainOperation` doc-comment, "concurrent callers for the same shell
  receive the same instance." Implemented now as: first caller CAS-publishes
  the new operation; subsequent callers observe the published reference
  and return early. Identical observable behavior.

**Alternatives considered**:

- **Keep the dictionary in addition to `IShell.Drain`.** Rejected. Two
  sources of truth for the same fact, with reconciliation cost on every
  drain start/end. Strictly worse than either choice alone.
- **Use `Lazy<DrainOperation>` inside `Shell` instead of CAS.** Rejected.
  `Lazy<>`'s thread-safety modes don't compose cleanly with the existing
  `Shell` state-machine CAS pattern; using the same primitive throughout
  the type (`Interlocked.CompareExchange`) is more consistent.

## R-003: How do management endpoints resolve `IShellRegistry`?

**Decision**: Endpoint handlers receive `IShellRegistry` via Minimal API's
parameter-binding by adding it to the lambda signature, e.g.
`group.MapPost("/reload/{name}", async (string name, IShellRegistry registry,
CancellationToken ct) => …)`. ASP.NET Core resolves it from
`HttpContext.RequestServices`, which for routes outside `ShellMiddleware`'s
prefix is the **root** service provider. That is correct: the management
endpoints are cross-shell by design and run at root scope.

**Rationale**:

- Per the existing CShells architecture (`AGENTS.md` line 13–15),
  `ShellMiddleware` swaps `HttpContext.RequestServices` only for routes it
  resolves to a shell. The management endpoints sit at a host-defined
  prefix (default `/_admin/shells`) outside any shell's path-prefix and
  outside any shell's host-binding, so they hit `RequestServices` = root
  provider.
- `IShellRegistry` is registered as a singleton at root scope by
  `AddCShells` (per `008` and prior). Resolving it from the root provider
  is the canonical pattern.
- No need for a custom `IRootServiceCollectionAccessor` indirection here —
  the root provider IS the request services for these routes.

**Alternatives considered**:

- **Capture `IShellRegistry` at install time via a closure.** Rejected.
  Forces the install method to take a `IServiceProvider`, which the
  consumer doesn't have at that point in `Program.cs` (the app isn't
  built yet — `app.MapShellManagementApi(...)` runs after `var app =
  builder.Build();`, so `app.Services` is available, but the convention
  in ASP.NET Core is parameter binding, not closures).
- **Expose endpoints as a `MapGroup`-attached `IFastEndpointsShellFeature`.**
  Rejected by the user up front (and codified in spec FR-001 / FR-002 / the
  package boundary). The endpoints are root-level, not shell-scoped.

## R-004: Should `ShellListQuery`/`ListAsync` query parameters be passed through directly?

**Decision**: The `GET /` endpoint accepts `cursor` (string) and `pageSize`
(int) as optional query-string parameters. The handler constructs
`new ShellListQuery(Cursor: cursor, Limit: pageSize ?? <default>)` and
forwards. Defaults match `ShellListQuery`'s own (page size of 100 is the
established default in the existing code; cursor is null on first page).
Out-of-range page sizes propagate through `ShellListQuery`'s existing
validation (which throws `ArgumentOutOfRangeException`); the handler maps
that to `400 Bad Request` per FR-013.

**Rationale**:

- `ShellListQuery` is an existing, validated input model. Reusing it means
  the management API picks up any future validation changes for free.
- Passing through opaque `cursor` strings preserves the registry's existing
  pagination contract — the management API has no opinion about cursor
  format.

**Alternatives considered**:

- **Accept `page` (1-based integer) instead of `cursor`.** Rejected. The
  existing pagination model is cursor-based; offset-based pagination would
  require a different registry API. Out of scope.
- **Add `?nameFilter=` for name-prefix filtering.** Rejected. Not in spec
  FR-005's enumerated routes, and `ShellListQuery` doesn't expose a name
  filter. Out of scope.

## R-005: Force-drain endpoint await pattern (per Q3 clarification → option A)

**Decision**: The handler walks `registry.GetAll(name)`, filters to shells
in `Deactivating` or `Draining` state, retrieves each one's `IShell.Drain`
(non-null per the FR-004 invariant), and runs:

```csharp
var inFlight = registry.GetAll(name)
    .Where(s => s.State is Deactivating or Draining)
    .ToArray();

if (inFlight.Length == 0)
    return Results.Problem(404, "No in-flight drain to force.");

var results = await Task.WhenAll(inFlight.Select(async shell =>
{
    var op = shell.Drain!; // FR-004 invariant guarantees non-null here
    await op.ForceAsync(ct);
    return await op.WaitAsync(ct);
}));

return Results.Ok(results.Select(MapDrainResult).ToArray());
```

`Task.WhenAll` on multiple `IDrainOperation.ForceAsync` + `WaitAsync` pairs
runs the forces concurrently, so two draining generations finish in
roughly `max(grace1, grace2)` rather than `grace1 + grace2`.

**Rationale**:

- Q3 clarification chose option A (await terminal state). The natural
  shape is `ForceAsync` then `WaitAsync` on each operation.
- Concurrent `ForceAsync` is safe: `IDrainOperation` is per-generation,
  and the operations target distinct `Shell` instances. No shared
  contention.
- Filtering on `State` is the right time-of-evaluation snapshot — a
  generation that transitions to `Drained` between snapshot and `ForceAsync`
  is benign (force is a no-op on terminal drain; result reports the
  natural terminal status). This race is documented in spec edge cases.

**Alternatives considered**:

- **Sequential await (`await foreach`).** Rejected. Doubles response time
  for two simultaneously-stuck drains; no benefit.
- **Fire-and-forget after `ForceAsync`, return current snapshots.**
  Rejected by Q3 — option B was explicitly **not** chosen.

## R-006: How are endpoint conventions composed (`MapGroup` vs individual `MapXxx`)?

**Decision**: Use `MapGroup(prefix)` to create a `RouteGroupBuilder` and
register the six routes on it. Return that `RouteGroupBuilder` from
`MapShellManagementApi`. `RouteGroupBuilder` already implements
`IEndpointConventionBuilder`, so chained calls
(`.RequireAuthorization(...)`, `.AddEndpointFilter(...)`,
`.WithTags(...)`, `.WithOpenApi()`) apply to all six routes uniformly.

**Rationale**:

- This is the canonical Minimal API pattern for a related-route group
  (e.g., `app.MapGroup("/api").RequireAuthorization()`).
- One auth chain at the group level is cleaner than per-route conventions.
- `RouteGroupBuilder` is the standard ASP.NET Core type — no custom
  builder, no package-specific glue.

**Alternatives considered**:

- **Return a custom builder type wrapping `RouteGroupBuilder`.** Rejected.
  Would force consumers to learn a new type and might not implement every
  convention they need. Spec FR-001 mandates the standard return type.
- **Return `void`; consumers tag routes individually if they want.**
  Rejected. SC-006 explicitly requires chained authorization on the
  install method's return value.

## R-007: How does the package handle `OpenApi`?

**Decision**: The package does **not** add `Produces<T>()` /
`WithOpenApi()` annotations of its own. Consumers chain `.WithOpenApi()`
on the returned `RouteGroupBuilder` if they want OpenAPI generation. This
matches FR-017 (out-of-scope: framework-generated OpenAPI documents
beyond what `WithOpenApi()` already produces).

**Rationale**:

- Each handler returns either `Results.Ok(typed-DTO)`, `Results.Problem(...)`,
  or `TypedResults.NotFound()`. ASP.NET Core's OpenAPI generator infers
  response types from `IResult` shapes when it scans endpoints, so a
  consumer who chains `.WithOpenApi()` gets reasonable defaults for free.
- Adding explicit `Produces<T>()` annotations would tie the package to a
  specific `Swashbuckle`/`Microsoft.AspNetCore.OpenApi` version. The
  framework-reference approach lets consumers pick.

**Alternatives considered**:

- **Use `TypedResults.Ok<T>(...)` so OpenAPI generation is more precise.**
  Considered as an option for the implementation phase — adopt
  `TypedResults` where it improves OpenAPI metadata, since the resulting
  endpoint registration is identical at runtime. Treat as an
  implementation refinement rather than a contract change.

## R-008: Sample integration cleanup

**Decision**: The Workbench currently has an inline
`app.MapGet("/_shells/status", ...)` ad-hoc admin endpoint in `Program.cs`.
After `009` ships, that endpoint is removed; `GET /_admin/shells/`
provides equivalent (and richer) functionality. The Workbench README's
diagnostic-section anchors are updated accordingly.

**Rationale**:

- Two parallel admin-style endpoints in the sample is confusing —
  defeats the purpose of the sample, which is to demonstrate "the way" to
  do something.
- The new `GET /_admin/shells/` returns more information (per-generation
  state and drain snapshots) than the inline endpoint, so this is
  strictly an upgrade for the sample.

**Alternatives considered**:

- **Keep the ad-hoc endpoint at `/_shells/status` as a "second style"
  example.** Rejected. Adds confusion without educational value.

## Summary

All eight decisions resolved without ambiguity. Phase 1 (data model +
contracts + quickstart) proceeds.
