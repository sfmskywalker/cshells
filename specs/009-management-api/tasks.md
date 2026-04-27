---
description: "Task list for Shell Management REST API — new optional CShells.Management.Api package + IShell.Drain abstraction extension"
---

# Tasks: Shell Management REST API

**Input**: Design documents from `/specs/009-management-api/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Scope**: Additive feature shipping a new optional NuGet package
(`CShells.Management.Api`) that maps six root-level Minimal API endpoints onto an
existing `IEndpointRouteBuilder`. Also adds one abstraction property
(`IShell.Drain`) and refactors `ShellRegistry.DrainAsync` to use it for
idempotency (deleting the existing private `_drainOps` dictionary).

**Tests**: Included (Constitution Principle V). Per SC-008, the test count
increases by at least 12 covering the six routes, error mappings, parallelism
validation, authorization passthrough, in-flight drain observation, and the
new `IShell.Drain` invariants.

**Organization**: Phase 1 sets up the new project. Phase 2 (Foundational) lands
the `IShell.Drain` abstraction extension, every internal DTO, the
`MapShellManagementApi` extension-method skeleton with stub handlers, and the
`ManagementApiFixture` — everything user-story phases depend on. Phases 3–9 are
one phase per user story (5× P1 + 2× P2 from the spec). Phase 10 is final
polish.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story this task belongs to (US1…US7). Setup, Foundational,
  and Polish phases have no story label.
- File paths are repo-root relative.

## Path Conventions

- **New project**: `src/CShells.Management.Api/`
- **Abstractions delta**: `src/CShells.Abstractions/Lifecycle/IShell.cs`
- **Implementation delta**: `src/CShells/Lifecycle/Shell.cs`,
  `src/CShells/Lifecycle/ShellRegistry.cs`
- **Tests**: `tests/CShells.Tests/Unit/Lifecycle/`,
  `tests/CShells.Tests/Integration/Management/`
- **Sample**: `samples/CShells.Workbench/`

---

## Phase 1: Setup — Project Initialization

**Purpose**: create the new library project and wire it into the solution.
After this phase the project compiles (empty) and is part of the solution
build.

- [X] T001 Create directory `src/CShells.Management.Api/`.
- [X] T002 Create `src/CShells.Management.Api/CShells.Management.Api.csproj` mirroring `src/CShells.AspNetCore/CShells.AspNetCore.csproj`'s shape: `<Authors>`, `<Description>` ("Optional REST management API for CShells. Exposes shell-reload and drain-lifecycle endpoints over HTTP for manual testing and demos. Authorization is the host's responsibility."), `<PackageTags>` (`shell;tenant;management;api;reload;drain;aspnetcore;minimal-api`), `<PackageProjectUrl>`, `<RepositoryUrl>`, `<PackageLicenseExpression>MIT</PackageLicenseExpression>`, `<PackageReadmeFile>README.md</PackageReadmeFile>`, `<PackageIcon>logo.png</PackageIcon>`, plus the standard `EmbedUntrackedSources`/`IncludeSymbols`/`SymbolPackageFormat` block. `<ItemGroup>` includes: a `<ProjectReference Include="..\CShells.Abstractions\CShells.Abstractions.csproj" />` and a `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. NO reference to `CShells.csproj`, `CShells.AspNetCore*`, or `CShells.FastEndpoints*`. The `<None Include="..\..\README.md" Pack="true" Link="README.md" />` and `<None Include="..\..\branding\logo.png" Pack="true" Link="logo.png" />` items follow the existing per-project pattern.
- [X] T003 [P] Create `src/CShells.Management.Api/README.md` (NuGet package readme) with sections: "Install", "Usage" (the one-line `app.MapShellManagementApi(...)` example), "Endpoints" (the six-row table from `contracts/ManagementApi.md`), "Authorization" (the FR-014 warning + chained `RequireAuthorization` snippet), and "What's intentionally missing" (FR-017 list).
- [X] T004 Modify `CShells.sln`: add the new `src/CShells.Management.Api/CShells.Management.Api.csproj` to the solution. Use `dotnet sln CShells.sln add src/CShells.Management.Api/CShells.Management.Api.csproj` to ensure GUIDs and configuration sections are well-formed.
- [X] T005 Run `dotnet build CShells.sln`. Confirm 0 warnings, 0 errors. The new project is empty but compiles.

**Checkpoint**: solution builds; new (empty) project is in place.

---

## Phase 2: Foundational — `IShell.Drain` extension + DTOs + extension-method skeleton

**Purpose**: land every prerequisite that user-story phases share — the
abstraction property, the registry refactor, every internal DTO, the
`MapShellManagementApi` skeleton with stub handlers, and the integration-test
fixture. After this phase the build is green, `IShell.Drain` works correctly in
unit tests, and the management endpoints are mapped (returning 501 Not
Implemented from each stub).

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

### Abstraction extension — `IShell.Drain` (FR-004)

- [X] T006 Modify `src/CShells.Abstractions/Lifecycle/IShell.cs`: add the `IDrainOperation? Drain { get; }` property with the XML doc-comment from `contracts/IShell.md` (state-binding invariant: non-null iff `Deactivating`/`Draining`/`Drained`; null otherwise). No other changes to `IShell`.
- [X] T007 Modify `src/CShells/Lifecycle/Shell.cs`:
  - Add `private DrainOperation? _drain;` field next to `_state` and `_disposeTask`.
  - Add `public IDrainOperation? Drain => Volatile.Read(ref _drain);` (matches the existing volatile read on `_state`).
  - Add `internal DrainOperation PublishDrain(DrainOperation candidate)` using `Interlocked.CompareExchange(ref _drain, candidate, null)` — returns the winner; idempotent on subsequent calls.
  - Modify `DisposeCoreAsync` to set `_drain` back to null *after* `ForceAdvanceAsync(Disposed)` returns and *before* the provider is disposed, breaking the `Shell ↔ DrainOperation` reference cycle for GC. Use `Volatile.Write(ref _drain, null)`.
- [X] T008 Modify `src/CShells/Lifecycle/ShellRegistry.cs`:
  - Delete the `private readonly ConcurrentDictionary<IShell, Lazy<DrainOperation>> _drainOps = new();` field and any usages.
  - Rewrite `DrainAsync(IShell shell, CancellationToken ct)`:
    1. Guard + cast to `Shell typedShell` as today.
    2. If `typedShell.Drain is { } existing` return that immediately.
    3. Otherwise build a `DrainOperation` via the existing `ResolveDrainPolicy()` / `ResolveGracePeriod()` / `ResolveDrainLogger()` helpers.
    4. CAS-publish via `var winner = typedShell.PublishDrain(candidate);`.
    5. If `ReferenceEquals(winner, candidate)` start the run (extracted helper `StartDrainRun(typedShell, candidate)` — same body as the existing `StartDrain` minus the `_drainOps.TryRemove` cleanup `ContinueWith`, which is no longer needed).
    6. Return `Task.FromResult<IDrainOperation>(winner)`.
  - Existing `StartDrain(Shell shell)` becomes `StartDrainRun(Shell shell, DrainOperation op)` and accepts the pre-built operation rather than constructing one. Adjust its single call site (the `_drainOps.GetOrAdd` line) accordingly.
- [X] T009 Run `dotnet build CShells.sln`. Confirm 0 warnings, 0 errors. The abstraction extension compiles end-to-end.

### Unit tests for `IShell.Drain` invariants (FR-004)

- [X] T010 [P] Create `tests/CShells.Tests/Unit/Lifecycle/ShellDrainPropertyTests.cs`. Cover:
  - `Drain_IsNull_BeforeDrainStarts` — fresh Shell in `Active` state has `Drain == null`.
  - `Drain_IsNonNull_DuringDeactivatingDrainingDrained` — call `registry.DrainAsync(shell)`, observe `shell.Drain` non-null while state is `Deactivating`/`Draining`; force completion, observe `Drain` still non-null while state is `Drained`.
  - `Drain_IsNull_AfterDispose` — drive shell to `Disposed`; observe `shell.Drain == null` (verifies the `DisposeCoreAsync` reset from T007).
  - `Drain_SameInstance_AsRegistryDrainAsyncReturn` — `registry.DrainAsync(shell)` and `shell.Drain` return the **same** reference (`ReferenceEquals` true). Enforces the FR-004 contract.
  - `Drain_SameInstance_AcrossConcurrentDrainAsyncCalls` — fire 16 concurrent `registry.DrainAsync(shell)` calls; assert all return the same reference and `shell.Drain` matches. Enforces the publish-once CAS contract from T007.
- [X] T011 Run `dotnet test --filter "FullyQualifiedName~ShellDrainPropertyTests"`. Confirm all 5 tests pass.

### Internal DTOs (`CShells.Management.Api/Models/`)

All types are `internal sealed record`. Field names use camelCase via System.Text.Json defaults; no attributes needed. Each is a separate file → all parallel.

- [X] T012 [P] Create `src/CShells.Management.Api/Models/DrainSnapshot.cs`: `internal sealed record DrainSnapshot(string Status, DateTimeOffset? Deadline);`.
- [X] T013 [P] Create `src/CShells.Management.Api/Models/ShellGenerationResponse.cs`: `internal sealed record ShellGenerationResponse(int Generation, string State, DateTimeOffset CreatedAt, DrainSnapshot? Drain);`.
- [X] T014 [P] Create `src/CShells.Management.Api/Models/BlueprintResponse.cs`: `internal sealed record BlueprintResponse(string Name, IReadOnlyList<string> Features, IReadOnlyDictionary<string, string> ConfigurationData);`. XML doc-comment notes ConfigurationData is included verbatim per FR-012a.
- [X] T015 [P] Create `src/CShells.Management.Api/Models/ShellListItem.cs`: `internal sealed record ShellListItem(string Name, BlueprintResponse? Blueprint, ShellGenerationResponse? Active);`.
- [X] T016 [P] Create `src/CShells.Management.Api/Models/ShellPageResponse.cs`: `internal sealed record ShellPageResponse(IReadOnlyList<ShellListItem> Items, string? NextCursor, int PageSize);`.
- [X] T017 [P] Create `src/CShells.Management.Api/Models/ShellDetailResponse.cs`: `internal sealed record ShellDetailResponse(string Name, BlueprintResponse? Blueprint, IReadOnlyList<ShellGenerationResponse> Generations);`.
- [X] T018 [P] Create `src/CShells.Management.Api/Models/ReloadResultResponse.cs`: contains `internal sealed record ReloadResultResponse(string Name, bool Success, ShellGenerationResponse? NewShell, DrainSnapshot? Drain, ErrorDescription? Error);` and `internal sealed record ErrorDescription(string Type, string Message);` in the same file.
- [X] T019 [P] Create `src/CShells.Management.Api/Models/DrainResultResponse.cs`: contains `internal sealed record DrainResultResponse(string Name, int Generation, string Status, TimeSpan ScopeWaitElapsed, int AbandonedScopeCount, IReadOnlyList<DrainHandlerResultResponse> HandlerResults);` and `internal sealed record DrainHandlerResultResponse(string HandlerType, string Outcome, TimeSpan Elapsed, string? ErrorMessage);` in the same file.

### Mapping helpers + result mapper

- [X] T020 Create `src/CShells.Management.Api/Endpoints/DtoMappers.cs` (`internal static class DtoMappers`): static methods
  - `static DrainSnapshot? MapDrain(IShell shell)` — returns null when `shell.Drain` is null, else `new DrainSnapshot(shell.Drain.Status.ToString(), shell.Drain.Deadline)`.
  - `static ShellGenerationResponse MapGeneration(IShell shell)` — combines descriptor + state + drain.
  - `static BlueprintResponse? MapBlueprint(ProvidedBlueprint? blueprint)` — null-passthrough; otherwise extracts name, feature names from `IShellBlueprint`, and `ConfigurationData` verbatim per FR-012a.
  - `static ShellListItem MapListItem(ShellSummary summary, IShellRegistry registry)` — uses `summary.ActiveGeneration` to look up the active shell via `registry.GetActive(summary.Name)` and maps it; calls `registry.GetBlueprintAsync(summary.Name)` synchronously where needed (or have the caller supply the blueprint). The exact signature is whatever maps cleanly inside the list-handler in T028.
  - `static ReloadResultResponse MapReload(ReloadResult result)` — derives `Success`, maps `NewShell`, snapshots the previous-generation drain via `result.Drain`.
  - `static DrainResultResponse MapDrainResult(DrainResult result)` — flat mapping including `HandlerResults`.
  - `static DrainHandlerResultResponse MapHandlerResult(DrainHandlerResult hr)`.
- [X] T021 Create `src/CShells.Management.Api/Endpoints/ResultMapper.cs` (`internal static class ResultMapper`): single method `static IResult MapException(Exception ex, HttpContext context)` implementing the FR-013 table:
  - `ShellBlueprintNotFoundException` → `Results.Problem(statusCode: 404, title: "Not Found", detail: ex.Message, instance: context.Request.Path)`.
  - `ShellBlueprintUnavailableException` → `Results.Problem(statusCode: 503, title: "Service Unavailable", detail: ex.Message, instance: context.Request.Path)`.
  - `OperationCanceledException` → `Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "Host is shutting down.", instance: context.Request.Path)`.
  - `ArgumentOutOfRangeException` → `Results.Problem(statusCode: 400, title: "Bad Request", detail: ex.Message, instance: context.Request.Path)`.
  - Default → `Results.Problem(statusCode: 500, title: "Internal Server Error", detail: ex.Message)` (rare; means an unmapped exception slipped through).

### Stub handlers (one file each — user-story phases replace the stubs)

Each stub registers its route on the supplied `RouteGroupBuilder` and returns `Results.Problem(statusCode: 501, title: "Not Implemented")` from the handler. The route registration is final — only the handler body is replaced in each user-story phase. Each handler exposes a single public-internal method `internal static RouteHandlerBuilder Map(RouteGroupBuilder group)` returning the `RouteHandlerBuilder` so the install method can compose conventions if needed (it doesn't, but the return type matches Minimal API conventions).

- [X] T022 [P] Create `src/CShells.Management.Api/Endpoints/ListShellsHandler.cs` (`internal static class ListShellsHandler`): `Map(group)` calls `group.MapGet("/", ...)` with a stub returning 501. Registered route: `GET {prefix}/`.
- [X] T023 [P] Create `src/CShells.Management.Api/Endpoints/GetShellHandler.cs` (`internal static class GetShellHandler`): `Map(group)` calls `group.MapGet("/{name}", ...)` with a stub. Registered route: `GET {prefix}/{name}`.
- [X] T024 [P] Create `src/CShells.Management.Api/Endpoints/GetBlueprintHandler.cs`: `group.MapGet("/{name}/blueprint", ...)` stub.
- [X] T025 [P] Create `src/CShells.Management.Api/Endpoints/ReloadShellHandler.cs`: `group.MapPost("/reload/{name}", ...)` stub.
- [X] T026 [P] Create `src/CShells.Management.Api/Endpoints/ReloadAllHandler.cs`: `group.MapPost("/reload-all", ...)` stub.
- [X] T027 [P] Create `src/CShells.Management.Api/Endpoints/ForceDrainHandler.cs`: `group.MapPost("/{name}/force-drain", ...)` stub.

### Public install method

- [X] T028 Create `src/CShells.Management.Api/EndpointRouteBuilderExtensions.cs` with namespace `CShells.Management.Api` and the single public extension method:

  ```csharp
  public static RouteGroupBuilder MapShellManagementApi(
      this IEndpointRouteBuilder endpoints,
      string prefix = "/_admin/shells")
  {
      Guard.Against.Null(endpoints);
      Guard.Against.NullOrWhiteSpace(prefix);

      var group = endpoints.MapGroup(prefix);
      ListShellsHandler.Map(group);
      GetShellHandler.Map(group);
      GetBlueprintHandler.Map(group);
      ReloadShellHandler.Map(group);
      ReloadAllHandler.Map(group);
      ForceDrainHandler.Map(group);
      return group;
  }
  ```

  XML doc-comment per FR-014 must state in plain language that:
  - the endpoints are unprotected by default;
  - `ConfigurationData` (which may contain secrets) is exposed verbatim;
  - production-style deployments must apply authorization by chaining `.RequireAuthorization(...)` (or equivalent) on the returned `RouteGroupBuilder`.

### Integration-test fixture

- [X] T029 Create `tests/CShells.Tests/Integration/Management/ManagementApiFixture.cs` (`internal sealed class ManagementApiFixture : IAsyncDisposable`): wraps a `WebApplicationFactory<Program>`-style minimal host built inline with `WebApplication.CreateBuilder(...)`. Constructor accepts `Action<CShellsBuilder>? configure` so individual test classes register their own shells. The fixture: registers CShells via `builder.Services.AddCShells(c => { configure?.Invoke(c); })`, calls `app.MapShellManagementApi("/admin")`, returns an `HttpClient` via `app.GetTestClient()`-equivalent. Implements `DisposeAsync` to stop and dispose the host. Helpers: `Task<T?> GetJsonAsync<T>(string path)`, `Task<HttpResponseMessage> PostAsync(string path)`, `Task<T> PostJsonAsync<T>(string path)` for the typed deserialization patterns used across all endpoint test classes.
- [X] T030 Run `dotnet build CShells.sln`. Confirm 0 warnings, 0 errors. All foundational types compile and the management API maps six routes (each returning 501 for now).

**Checkpoint**: build is green. `IShell.Drain` is observable and tested.
The management endpoints are mapped under `/_admin/shells` and return 501.
The fixture is ready for user-story tests. No user-story functionality
yet.

---

## Phase 3: User Story 1 — Reload one shell over HTTP (Priority: P1) 🎯 MVP

**Goal**: `POST /reload/{name}` reloads a single shell and returns a structured
`ReloadResultResponse` with the new generation + previous-generation drain
snapshot. Errors map per FR-013.

**Independent Test**: with `ManagementApiFixture` running with one shell `acme`
already activated, `POST /admin/reload/acme` returns 200 with `success: true`,
`newShell.generation == 2`, and a non-null `drain` object.

### Implementation

- [X] T031 [US1] Replace the stub in `src/CShells.Management.Api/Endpoints/ReloadShellHandler.cs` with the real handler:
  - Lambda parameters: `(string name, IShellRegistry registry, HttpContext ctx, CancellationToken ct)`.
  - Body: `try { var result = await registry.ReloadAsync(name, ct); return Results.Ok(DtoMappers.MapReload(result)); } catch (Exception ex) { return ResultMapper.MapException(ex, ctx); }`.
  - Add `.WithName("ReloadShell")` for OpenAPI inference.

### Tests

- [X] T032 [P] [US1] Create `tests/CShells.Tests/Integration/Management/ReloadShellEndpointTests.cs`. Cover:
  - `Reload_KnownActiveShell_Returns200_WithAdvancedGeneration` (US1 acceptance 1) — pre-activate `acme` with a stub blueprint; call `POST /admin/reload/acme`; assert `success: true`, `newShell.generation == 2`, `drain` non-null with `status` ∈ {`Pending`, `Completed`, `Forced`}.
  - `Reload_UnknownName_Returns404_WithProblemDetails` (US1 acceptance 2) — call `POST /admin/reload/does-not-exist`; assert 404 + RFC 7807 body whose `detail` names the shell.
  - `Reload_ProviderUnavailable_Returns503` (US1 acceptance 3) — register a `ThrowingShellBlueprintProvider` (test-only) that throws on lookup; call reload; assert 503 + problem-details body. Asserts `ShellBlueprintUnavailableException` mapping from FR-013.
  - `Reload_ShellWasNeverActivated_StillReturnsSuccess` (edge case) — `ReloadAsync` on a known-but-inactive blueprint behaves like `ActivateAsync`; assert `newShell.generation == 1`, `drain` null (no previous gen to drain).
- [X] T033 [US1] Run `dotnet test --filter "FullyQualifiedName~ReloadShellEndpointTests"`. Confirm all 4 tests pass.

**Checkpoint**: SC-002 partially satisfied (reload-one works end-to-end with
structured response). US1 acceptance scenarios 1–3 verified.

---

## Phase 4: User Story 2 — Reload-all (Priority: P1)

**Goal**: `POST /reload-all` reloads every active shell with default
parallelism and returns an array of `ReloadResultResponse`. Partial failures
do not abort the batch.

**Independent Test**: with the fixture running with three active shells, `POST
/admin/reload-all` returns 200 with a three-element array, each entry
`success: true`.

### Implementation

- [X] T034 [US2] Replace the stub in `src/CShells.Management.Api/Endpoints/ReloadAllHandler.cs`:
  - Lambda parameters: `(int? maxDegreeOfParallelism, IShellRegistry registry, HttpContext ctx, CancellationToken ct)`.
  - When `maxDegreeOfParallelism` is null → `var opts = new ReloadOptions();` (default 8). When non-null → `var opts = new ReloadOptions(maxDegreeOfParallelism.Value); opts.EnsureValid();` — `ArgumentOutOfRangeException` propagates to the catch and is mapped to 400.
  - Body: `try { var results = await registry.ReloadActiveAsync(opts, ct); return Results.Ok(results.Select(DtoMappers.MapReload).ToArray()); } catch (Exception ex) { return ResultMapper.MapException(ex, ctx); }`.
  - `.WithName("ReloadAll")`.

### Tests

- [X] T035 [P] [US2] Create `tests/CShells.Tests/Integration/Management/ReloadAllEndpointTests.cs`. Cover:
  - `ReloadAll_ThreeActiveShells_Returns200_WithThreeAdvancedEntries` (US2 acceptance 1).
  - `ReloadAll_OneShellFails_OthersStillReload_StatusStill200` (US2 acceptance 2) — register one shell whose blueprint provider throws on lookup mid-batch; assert overall 200, failing entry's `error.type` is `ShellBlueprintUnavailableException`, other entries `success: true`.
  - `ReloadAll_NoActiveShells_Returns200_WithEmptyArray` (US2 acceptance 3) — registry has blueprints but none active; assert 200 + `[]`.
  - `ReloadAll_DuringHostShutdown_Returns503` — pass a pre-cancelled token via `HttpClient.SendAsync` cancellation; assert 503.
- [X] T036 [US2] Run `dotnet test --filter "FullyQualifiedName~ReloadAllEndpointTests"`. Confirm all 4 tests pass.

**Checkpoint**: SC-003 satisfied. US2 acceptance scenarios verified. Parallelism
override (US6) deferred to Phase 8.

---

## Phase 5: User Story 3 — Inspect shells & per-generation drain state (Priority: P1)

**Goal**: three read endpoints — `GET /`, `GET /{name}`, `GET /{name}/blueprint` —
expose the catalogue, focused-view, and blueprint-only views. The focused view's
`generations` array surfaces per-generation lifecycle state and drain snapshots
(via `IShell.Drain` from Phase 2), enabling real-time observation of drains.

**Independent Test**: reload a shell, immediately `GET /admin/{name}` shows two
generations (active + draining) with the previous gen's `drain.status`
non-null; poll until only the active gen remains.

### Implementation

- [X] T037 [P] [US3] Replace the stub in `src/CShells.Management.Api/Endpoints/ListShellsHandler.cs`:
  - Lambda parameters: `(string? cursor, int? pageSize, IShellRegistry registry, HttpContext ctx, CancellationToken ct)`.
  - Build `var query = new ShellListQuery(Cursor: cursor, Limit: pageSize ?? 100);`.
  - `var page = await registry.ListAsync(query, ct);`.
  - Map each `ShellSummary` to a `ShellListItem` via `DtoMappers.MapListItem`. The mapper resolves the active `IShell` (if any) via `registry.GetActive(summary.Name)` and the registered blueprint via `registry.GetBlueprintAsync(summary.Name, ct)`.
  - Return `Results.Ok(new ShellPageResponse(items, page.NextCursor, query.Limit))`.
  - `.WithName("ListShells")`.
- [X] T038 [P] [US3] Replace the stub in `src/CShells.Management.Api/Endpoints/GetShellHandler.cs`:
  - Lambda parameters: `(string name, IShellRegistry registry, HttpContext ctx, CancellationToken ct)`.
  - Lookup blueprint via `registry.GetBlueprintAsync(name, ct)`. Lookup all generations via `registry.GetAll(name)`.
  - If both are empty (no blueprint AND no live generations), return `Results.Problem(statusCode: 404, title: "Not Found", detail: $"Shell '{name}' has no blueprint and no live generations.", instance: ctx.Request.Path)`.
  - Map generations via `DtoMappers.MapGeneration` (each gen gets `drain` populated when state is `Deactivating`/`Draining`/`Drained`).
  - Return `Results.Ok(new ShellDetailResponse(name, blueprintDto, generationsDtoArray))`.
  - `.WithName("GetShell")`.
- [X] T039 [P] [US3] Replace the stub in `src/CShells.Management.Api/Endpoints/GetBlueprintHandler.cs`:
  - Lambda parameters: `(string name, IShellRegistry registry, HttpContext ctx, CancellationToken ct)`.
  - `var bp = await registry.GetBlueprintAsync(name, ct);` — note the existing doc-comment that this method propagates provider exceptions raw, so wrap in try/catch and map via `ResultMapper`.
  - If `bp` is null, return 404 problem-details.
  - Otherwise map via `DtoMappers.MapBlueprint(bp)` and return `Results.Ok(...)`.
  - `.WithName("GetBlueprint")`.

### Tests

- [X] T040 [P] [US3] Create `tests/CShells.Tests/Integration/Management/ListShellsEndpointTests.cs`. Cover:
  - `List_FiveBlueprintsThreeActive_Returns200_WithFiveItems` (US3 acceptance 1) — assert `items.Length == 5`, three of them have non-null `active`, two have null `active`.
  - `List_PageSize2_Returns2ItemsAndCursor` — paging smoke test.
  - `List_PageSizeOutOfRange_Returns400` — pass `pageSize=-1` (or whatever is out of `ShellListQuery`'s validated range); expect 400.
  - `List_EmptyRegistry_Returns200_EmptyArray` (edge case) — no blueprints registered; assert `items: []`, `nextCursor: null`.
- [X] T041 [P] [US3] Create `tests/CShells.Tests/Integration/Management/GetShellEndpointTests.cs`. Cover:
  - `GetShell_DuringInflightReload_ShowsTwoGenerations_PreviousGenHasDrainSnapshot` (US3 acceptance 2) — register a shell with a deliberately-slow drain handler; trigger reload via the API; immediately `GET /admin/acme`; assert `generations.Length == 2`, the non-active gen's `drain.status` is `Pending`, the active gen's `drain` is null.
  - `GetShell_AfterDrainCompletes_ShowsOnlyActive` (US3 acceptance 3) — same setup but poll until `generations.Length == 1`.
  - `GetShell_RegisteredButInactive_ShowsBlueprint_EmptyGenerations` (US3 acceptance 4).
  - `GetShell_UnknownName_Returns404` (US3 acceptance 5).
- [X] T042 [P] [US3] Create `tests/CShells.Tests/Integration/Management/GetBlueprintEndpointTests.cs`. Cover:
  - `GetBlueprint_RegisteredName_Returns200_WithFeaturesAndConfigurationData` (US3 acceptance 6) — assert `configurationData` map matches the registered values verbatim (FR-012a).
  - `GetBlueprint_DoesNotActivateShell` — call `GET /admin/{name}/blueprint`, then assert `registry.GetActive(name)` returns null.
  - `GetBlueprint_UnknownName_Returns404`.
- [X] T043 [US3] Run `dotnet test --filter "FullyQualifiedName~ListShellsEndpointTests|GetShellEndpointTests|GetBlueprintEndpointTests"`. Confirm all 11 tests pass.

**Checkpoint**: SC-002, SC-004 satisfied. US3 acceptance scenarios verified.
Per-generation drain observability is end-to-end: reload + poll reveals
state transitions through `Deactivating`/`Draining`/`Drained`.

---

## Phase 6: User Story 4 — Force-drain endpoint (Priority: P1)

**Goal**: `POST /{name}/force-drain` forces every in-flight drain
(`Deactivating`/`Draining` generations) on the named shell to terminate
immediately, returning an array of `DrainResultResponse`.

**Independent Test**: register a slow drain handler; trigger two consecutive
reloads to leave two draining generations; `POST /admin/{name}/force-drain`
returns 200 with a two-element array, each entry's `status: Forced` (or
`Completed`); subsequent `GET /admin/{name}` shows only the active gen.

### Implementation

- [X] T044 [US4] Replace the stub in `src/CShells.Management.Api/Endpoints/ForceDrainHandler.cs`:
  - Lambda parameters: `(string name, IShellRegistry registry, HttpContext ctx, CancellationToken ct)`.
  - Body:
    1. `var allGens = registry.GetAll(name);`.
    2. If `allGens.Count == 0` and `await registry.GetBlueprintAsync(name, ct) is null` → 404 ("unknown shell name").
    3. `var inFlight = allGens.Where(s => s.State is ShellLifecycleState.Deactivating or ShellLifecycleState.Draining).ToArray();`.
    4. If `inFlight.Length == 0` → 404 with detail `"No in-flight drain to force for shell '{name}'."`.
    5. `var results = await Task.WhenAll(inFlight.Select(async shell => { var op = shell.Drain!; await op.ForceAsync(ct); return await op.WaitAsync(ct); }));`.
    6. Return `Results.Ok(results.Select(DtoMappers.MapDrainResult).ToArray())`.
  - Wrap in try/catch with `ResultMapper.MapException`.
  - `.WithName("ForceDrain")`.

### Tests

- [X] T045 [P] [US4] Create `tests/CShells.Tests/Integration/Management/ForceDrainEndpointTests.cs`. Cover:
  - `ForceDrain_OneInflightGeneration_Returns200_WithSingleResult_StatusForcedOrCompleted` (US4 acceptance 1).
  - `ForceDrain_TwoInflightGenerations_Returns200_WithTwoResults` (US4 acceptance 2) — two consecutive reloads leave two draining gens; assert array length 2; each entry `status` ∈ {`Forced`, `Completed`}.
  - `ForceDrain_NoInflightDrain_Returns404` (US4 acceptance 3) — only the active gen exists; assert 404 with detail naming "no in-flight drain to force".
  - `ForceDrain_UnknownName_Returns404` (US4 acceptance 4).
  - `ForceDrain_OnlyDrainedGenerations_Returns404` (edge case) — drive previous gens to `Drained` first; assert 404 (since `Drained` isn't `Deactivating`/`Draining`).
  - `ForceDrain_RaceWithNaturalCompletion_ReturnsCompleted` (edge case) — register a drain handler that completes during the `WhenAll` await; assert at least one result has `status: Completed` rather than `Forced` (depending on timing).
- [X] T046 [US4] Run `dotnet test --filter "FullyQualifiedName~ForceDrainEndpointTests"`. Confirm all 6 tests pass.

**Checkpoint**: SC-005 satisfied (force-drain in one HTTP request, array of
`DrainResult`). US4 acceptance scenarios verified including the
two-draining-generation case.

---

## Phase 7: User Story 5 — Authorization passthrough (Priority: P1)

**Goal**: confirm that `RouteGroupBuilder` chaining works exactly as ASP.NET
Core convention dictates: a host that wraps `MapShellManagementApi(...)` with
`.RequireAuthorization(...)` blocks anonymous callers across **all** six
routes, and a host that chains `.AddEndpointFilter(...)` runs the filter
before each handler.

**Independent Test**: build a fixture variant whose install line chains
`.RequireAuthorization()` with a default-deny policy. Anonymous requests to
any management route return 401; authenticated requests return what an
unprotected fixture would.

### Implementation

No new production code — the install method already returns
`RouteGroupBuilder` (T028). This phase is verification-only.

### Tests

- [X] T047 [P] [US5] Create `tests/CShells.Tests/Integration/Management/AuthorizationPassthroughTests.cs`. Cover:
  - `RequireAuthorization_BlocksUnauthenticatedAccess_ToAllSixRoutes` (US5 acceptance 1) — fixture with `.AddAuthorization(...)` + default-deny policy + chained `.RequireAuthorization()`. Issue unauthenticated requests to each of `GET /`, `GET /{name}`, `GET /{name}/blueprint`, `POST /reload/{name}`, `POST /reload-all`, `POST /{name}/force-drain`. Assert all return 401. Verify the registry was never invoked (e.g., by registering a strict-mock or counting registry calls via a wrapping decorator).
  - `RequireAuthorization_AllowsAuthenticatedAccess_MatchesUnprotectedResponse` (US5 acceptance 2) — with valid credentials (test scheme), assert at least one endpoint returns 200 with the same body shape an unprotected fixture would.
  - `AddEndpointFilter_RunsBeforeHandler` (US5 acceptance 3) — chain a filter that increments a counter; issue any management request; assert the counter incremented before the registry was consulted.
- [X] T048 [US5] Run `dotnet test --filter "FullyQualifiedName~AuthorizationPassthroughTests"`. Confirm all 3 tests pass.

**Checkpoint**: SC-006 satisfied (chained authorization composes uniformly).
US5 acceptance scenarios verified.

---

## Phase 8: User Story 6 — Parallelism query string (Priority: P2)

**Goal**: `POST /reload-all?maxDegreeOfParallelism=N` validates `N` against
the `[1, 64]` range from `ReloadOptions`. Out-of-range or non-integer values
return 400 with a problem-details body naming the parameter.

**Independent Test**: `?maxDegreeOfParallelism=1` succeeds; `=0`, `=65`, and
`=abc` each return 400 with descriptive problem-details.

### Implementation

The validation is already in T034 via `ReloadOptions.EnsureValid()` →
`ArgumentOutOfRangeException` → `ResultMapper` → 400. This phase verifies
end-to-end.

### Tests

- [X] T049 [P] [US6] Add to `tests/CShells.Tests/Integration/Management/ReloadAllEndpointTests.cs` (extend the file from Phase 4 with new tests):
  - `ReloadAll_WithParallelism1_Returns200` (US6 acceptance 1) — assert success.
  - `ReloadAll_WithParallelism0_Returns400_DetailNamesParameter` (US6 acceptance 2 part 1).
  - `ReloadAll_WithParallelism65_Returns400` (US6 acceptance 2 part 2).
  - `ReloadAll_WithParallelismAbc_Returns400` — Minimal API parameter binding rejects non-integer at bind time; assert 400 with detail referencing the parameter (the framework's binding-failure message is acceptable).
  - `ReloadAll_NoParallelismQuery_UsesDefault8` (US6 acceptance 3) — provide a hook in the fixture that captures the `ReloadOptions` passed into a wrapping `IShellRegistry` decorator; assert default `MaxDegreeOfParallelism == 8`.
- [X] T050 [US6] Run `dotnet test --filter "FullyQualifiedName~ReloadAllEndpointTests"`. Confirm the 5 new tests + the 4 from Phase 4 (9 total) pass.

**Checkpoint**: SC-007 satisfied (parameter-named bad-request response).
US6 acceptance scenarios verified.

---

## Phase 9: User Story 7 — Workbench sample integration (Priority: P2)

**Goal**: the `samples/CShells.Workbench` host wires the management endpoints
unprotected (sample-only, code-comment flagged), removes the existing
ad-hoc `MapGet("/_shells/status", ...)` admin endpoint that the new module
supersedes, and gains a "Manual Testing via the Management API" section in
its README with `curl` examples for each endpoint.

**Independent Test**: `cd samples/CShells.Workbench && dotnet run`; issue
`curl -X POST http://localhost:5000/_admin/shells/reload-all`; observe
all Workbench shells reload with structured per-shell outcomes.

### Implementation

- [X] T051 [US7] Modify `samples/CShells.Workbench/CShells.Workbench.csproj`: add `<ProjectReference Include="..\..\src\CShells.Management.Api\CShells.Management.Api.csproj" />`.
- [X] T052 [US7] Modify `samples/CShells.Workbench/Program.cs`:
  - Add `using CShells.Management.Api;` at top.
  - Remove the inline `app.MapGet("/_shells/status", ...)` block (per research R-008 — superseded by `GET /_admin/shells/`). Also remove any helper methods used only by that endpoint.
  - After `app.MapShells()`, add: `app.MapShellManagementApi("/_admin/shells");` preceded by a one-line comment: `// Sample-only: management endpoints are unprotected. In production, chain .RequireAuthorization(...) on the returned RouteGroupBuilder.`.
- [X] T053 [P] [US7] Modify `samples/CShells.Workbench/README.md`: add a new section "Manual Testing via the Management API" near the existing "Running" / "Endpoints" section. Include:
  - One-paragraph description of the unprotected install.
  - `curl` examples (one each, copy-paste runnable) for: list (`GET /_admin/shells/`), focused-view (`GET /_admin/shells/Default`), reload-single (`POST /_admin/shells/reload/Default`), reload-all (`POST /_admin/shells/reload-all`), force-drain (`POST /_admin/shells/Default/force-drain`).
  - A note that the Workbench install is unprotected sample-only and that production hosts must chain `.RequireAuthorization(...)`.
- [X] T054 [US7] Run `cd samples/CShells.Workbench && dotnet run` in one terminal. In another, issue each `curl` from the README; assert all return 200 (or expected status codes per the contract). Stop the host. Capture the outputs in the PR description for review.

### Tests

- [X] T055 [P] [US7] Verify the Workbench E2E suite still passes: `dotnet test tests/CShells.Tests.EndToEnd/CShells.Tests.EndToEnd.csproj`. (The E2E tests should not depend on the removed `/_shells/status` endpoint; if any do, update them to use `GET /_admin/shells/` instead.)

**Checkpoint**: SC-001 (one-line install), SC-008 (sample on-ramp) satisfied.
US7 acceptance scenarios verified.

---

## Phase 10: Polish & Cross-Cutting

**Purpose**: final quality pass — full build/test, doc-comment audit,
constitution recheck, agent context refresh.

- [X] T056 [P] Audit XML doc-comments on every public type/member in `src/CShells.Management.Api/`: every public method/property/class has `<summary>` (and `<param>`/`<returns>`/`<remarks>` where applicable) per Constitution Principle VI. Verify the install method's `<remarks>` includes the FR-014 authorization warning AND the FR-012a ConfigurationData-exposure note.
- [X] T057 [P] Verify the new package's NuGet metadata: open `src/CShells.Management.Api/CShells.Management.Api.csproj` and confirm `<Description>`, `<PackageTags>`, `<PackageLicenseExpression>`, `<PackageReadmeFile>`, `<PackageIcon>` are populated and the per-project README is included via the parent `Directory.Build.props` `<ItemGroup>` (auto-includes `$(MSBuildProjectDirectory)\README.md`).
- [X] T058 [P] Run `grep -rn "_drainOps\|ConcurrentDictionary<IShell" src/CShells/ --include="*.cs"`. Confirm zero matches (validates T008's deletion landed cleanly).
- [X] T059 [P] Run `grep -rn "MapGet.*_shells/status" samples/ --include="*.cs"`. Confirm zero matches (validates T052's removal of the ad-hoc endpoint).
- [X] T060 Final `dotnet build CShells.sln && dotnet test CShells.sln`. Confirm 0 warnings, 0 errors, all tests pass. Verify the test count increased by **at least 12** vs. `main` per SC-008 (`dotnet test 2>&1 | grep -E "Total tests|Passed"`).
- [X] T061 Run quickstart validation per `specs/009-management-api/quickstart.md`: build the doc'd one-line install in a scratch `Program.cs`, hit each documented endpoint with `curl`, confirm responses match `contracts/ManagementApi.md`. (May be combined with T054.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies.
- **Phase 2 (Foundational)**: depends on Phase 1. Atomic — every task in Phase 2 must land before user-story phases run because the build must be green and the management API must be mapped (even if returning 501).
- **Phase 3 (US1)**: depends on Phase 2.
- **Phase 4 (US2)**: depends on Phase 2. Independent of Phase 3.
- **Phase 5 (US3)**: depends on Phase 2. Independent of Phases 3–4. Note: T040–T043 use the fixture from T029.
- **Phase 6 (US4)**: depends on Phase 2 (specifically T006–T010 for the `IShell.Drain` foundation). Independent of Phases 3–5.
- **Phase 7 (US5)**: depends on Phase 2 (the install method exists). Best run AFTER Phases 3–6 so the auth tests have real (non-501) endpoints to probe.
- **Phase 8 (US6)**: depends on Phase 4 (extends `ReloadAllEndpointTests`).
- **Phase 9 (US7)**: depends on Phases 3–6 (the Workbench needs working endpoints).
- **Phase 10 (Polish)**: depends on Phases 1–9.

### Cross-task file overlap (serialize)

- `src/CShells.Abstractions/Lifecycle/IShell.cs` — touched only by T006.
- `src/CShells/Lifecycle/Shell.cs` — touched only by T007.
- `src/CShells/Lifecycle/ShellRegistry.cs` — touched only by T008.
- Each handler file `src/CShells.Management.Api/Endpoints/*Handler.cs` — created in Phase 2 (T022–T027) and modified once per its owning user-story phase (T031, T034, T037–T039, T044). No two phases touch the same handler.
- `src/CShells.Management.Api/EndpointRouteBuilderExtensions.cs` — written once in T028; not modified afterwards.
- `tests/CShells.Tests/Integration/Management/ReloadAllEndpointTests.cs` — created in T035; extended in T049 (parallelism scenarios).
- `samples/CShells.Workbench/Program.cs` — touched only by T052.

### Parallel Opportunities

- **Phase 1**: T003 in parallel with T002 once the directory exists from T001; T004 follows T002.
- **Phase 2 DTOs (T012–T019)**: all eight DTO files are independent → all parallel.
- **Phase 2 stub handlers (T022–T027)**: six independent files → all parallel.
- **Phase 5 read-endpoint handlers (T037–T039)**: three independent files → parallel. Tests (T040–T042) likewise parallel.
- **Phases 3–6 are mutually independent** post-Foundational: with three developers, US1, US2, and US3+US4 can be tackled simultaneously after Phase 2.
- **Phase 10 polish (T056–T059)**: independent grep/audit tasks → parallel.

### Within Each User Story

- Implementation precedes tests (handler must exist before tests can hit a non-501 response). Tests still drive verification — write tests immediately after the implementation task and observe failures map to spec acceptance scenarios as the impl is iterated.
- Each user-story phase ends with a `dotnet test --filter` run to confirm the story's tests all pass before moving on.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Tasks T012–T019 (eight DTO files) — all independent, all parallel:
Task: "Create src/CShells.Management.Api/Models/DrainSnapshot.cs"
Task: "Create src/CShells.Management.Api/Models/ShellGenerationResponse.cs"
Task: "Create src/CShells.Management.Api/Models/BlueprintResponse.cs"
Task: "Create src/CShells.Management.Api/Models/ShellListItem.cs"
Task: "Create src/CShells.Management.Api/Models/ShellPageResponse.cs"
Task: "Create src/CShells.Management.Api/Models/ShellDetailResponse.cs"
Task: "Create src/CShells.Management.Api/Models/ReloadResultResponse.cs"
Task: "Create src/CShells.Management.Api/Models/DrainResultResponse.cs"

# Tasks T022–T027 (six stub handler files) — all independent, all parallel:
Task: "Create src/CShells.Management.Api/Endpoints/ListShellsHandler.cs"
Task: "Create src/CShells.Management.Api/Endpoints/GetShellHandler.cs"
Task: "Create src/CShells.Management.Api/Endpoints/GetBlueprintHandler.cs"
Task: "Create src/CShells.Management.Api/Endpoints/ReloadShellHandler.cs"
Task: "Create src/CShells.Management.Api/Endpoints/ReloadAllHandler.cs"
Task: "Create src/CShells.Management.Api/Endpoints/ForceDrainHandler.cs"
```

---

## Implementation Strategy

### MVP First — User Story 1 only

For the smallest valuable demo:

1. **Phase 1**: Setup (T001–T005).
2. **Phase 2**: Foundational (T006–T030) — must complete; gates everything.
3. **Phase 3**: User Story 1 (T031–T033).
4. **STOP and VALIDATE**: `dotnet test --filter ReloadShellEndpointTests` passes; `curl -X POST /admin/reload/{name}` works end-to-end on a scratch host.

That's reload-one over HTTP — the headline scenario (US1, P1).

### Incremental Delivery (recommended)

After the MVP, add user stories one at a time, each as a separate
test-passing checkpoint:

1. MVP → US1 ✅
2. + US2 (reload-all) → SC-003 ✅
3. + US3 (read endpoints with drain observability) → SC-004 ✅
4. + US4 (force-drain) → SC-005 ✅
5. + US5 (auth passthrough verified) → SC-006 ✅
6. + US6 (parallelism validation) → SC-007 ✅
7. + US7 (Workbench wired) → SC-001 / SC-008 on-ramp ✅
8. + Phase 10 (polish) → merge gate.

### Single-developer one-shot

For one developer in one session, follow the phase order strictly. The
foundational phase is the heaviest (T006–T030 — abstraction extension +
8 DTOs + 6 stub handlers + fixture + skeleton + build/test). Each
subsequent phase is small (1–6 tasks).

### Parallel team strategy

With three developers after Phase 2 lands:

- Dev A: US1 (T031–T033), then US4 (T044–T046).
- Dev B: US2 (T034–T036), then US6 (T049–T050).
- Dev C: US3 (T037–T043), then US7 (T051–T055).
- All three converge on US5 (T047–T048) and Phase 10.

---

## Notes

- Per Constitution Principle VI: do not retain `[Obsolete]` shims for the
  removed `_drainOps` field in `ShellRegistry` (it was private; no
  migration story needed).
- The `IShell.Drain` extension is additive on the abstraction; existing
  callers see no breaking change. The `ShellRegistry.DrainAsync` semantics
  are preserved (idempotent, returns the same instance) — implementation
  changes only.
- The new package contributes NO service registrations. Hosts wire it via
  `app.MapShellManagementApi(...)` only — there is no
  `services.AddCShellsManagementApi()` and there must not be one (FR-003).
- The `RouteGroupBuilder` return type from `MapShellManagementApi` is
  load-bearing for SC-006 (chained authorization). Do not change to `void`
  or to a custom builder type.
- All non-2xx responses use RFC 7807 problem-details via
  `Results.Problem(...)` per FR-013. Do not return raw error strings or
  custom error envelopes.
