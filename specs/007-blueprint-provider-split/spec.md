# Feature Specification: Scale-Ready Blueprint Provider/Manager Split

**Feature Branch**: `007-blueprint-provider-split`
**Created**: 2026-04-24
**Status**: Draft
**Input**: User description: "Split shell blueprint management into a lazy, paginated provider for catalogue enumeration and an optional manager for mutable stores, with lazy activation from the registry."

## Overview

After the lifecycle overhaul in feature `006`, the shell registry holds every blueprint in
memory and expects host code to eagerly register them imperatively. That model does not scale:
real deployments may have tens or hundreds of thousands of named shells (for example one per
tenant), only a few thousand of which can realistically be materialized as live
`IServiceProvider` instances at any moment given per-shell memory cost. Eager enumeration also
conflates two concerns that naturally diverge in practice — **sourcing** blueprints from a
catalogue (code, configuration, database) and **mutating** that catalogue at runtime — because
many sources are read-only (configuration files) while others are read/write (a blueprint
table).

This feature replaces the registration-centric surface with a scale-ready, source-agnostic
model:

- A **blueprint provider** owns catalogue storage and enumeration. It answers "give me the
  blueprint for name `X`" on demand and supports paginated listing. Multiple providers can
  coexist under a composite.
- An **optional blueprint manager** lives alongside a provider when the underlying source
  accepts writes (create / update / delete). Providers whose source is read-only simply do
  not register a manager.
- The **registry** becomes an index of *active* shells — activation is lazy, driven by the
  first request (or explicit `GetOrActivateAsync`). Startup cost is bounded by the warm set,
  not the catalogue size. The registry delegates blueprint lookup and catalogue listing to
  the provider.

**This feature is a clean overhaul.** The legacy imperative surface from feature `006` —
`IShellRegistry.RegisterBlueprint`, `GetBlueprint`, `GetBlueprintNames`, `ReloadAllAsync` —
is removed entirely. Host code using `AddShell(...)` continues to work unchanged; the
underlying wiring routes through a built-in in-memory provider. Downstream integrations
(`CShells.AspNetCore`, `CShells.FastEndpoints`, `CShells.Providers.FluentStorage`) are
migrated in-place.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Host scales to hundreds of thousands of blueprints without paying for unused shells (Priority: P1)

A host registers a blueprint provider backed by a database containing 100 000 tenant shells.
At host startup the registry does not enumerate the full catalogue — startup time and memory
scale with the pre-warmed set (typically zero), not with the catalogue size. When the first
request for tenant `acme-42` arrives, the registry looks up its blueprint on-demand, builds
generation 1, and serves the request. Subsequent requests for other tenants each activate a
single shell the first time they are seen. Tenants that are never touched are never built.

**Why this priority**: This is the motivating requirement for the whole feature. Without lazy
activation, CShells cannot be used for multi-tenant deployments at any meaningful scale, and
the other capabilities (mutation, composition, listing) are moot.

**Independent Test**: Register a stub provider that returns a blueprint for any name matching
a prefix, assert startup completes in constant time regardless of how many names the provider
claims to have, then issue requests for a handful of names and assert each one activates
exactly once on first touch.

**Acceptance Scenarios**:

1. **Given** a provider reporting a catalogue of 100 000 blueprints, **When** the host starts,
   **Then** startup completes without the provider being asked to enumerate the catalogue and
   the registry holds zero active shells.
2. **Given** no active shell for name `acme-42`, **When** `GetOrActivateAsync("acme-42")` is
   called, **Then** the registry calls the provider's lookup exactly once, builds generation
   1, promotes it to `Active`, and returns it.
3. **Given** an already-active shell for name `acme-42`, **When** `GetOrActivateAsync` is
   called again, **Then** the provider is not re-queried and the existing shell is returned.
4. **Given** 100 concurrent requests for an inactive name, **When** they all call
   `GetOrActivateAsync` simultaneously, **Then** the provider is queried exactly once, the
   shell is built exactly once, and every caller receives the same shell instance.
5. **Given** the provider's lookup throws (e.g., database unreachable), **When**
   `GetOrActivateAsync` runs, **Then** the caller observes a distinct exception type
   (`ShellBlueprintUnavailableException`) and the registry retains no partial state for that
   name — a subsequent call retries the lookup.
6. **Given** the provider returns `null` for a requested name, **When** `GetOrActivateAsync`
   runs, **Then** the caller observes a distinct exception type
   (`ShellBlueprintNotFoundException`) and, as above, the registry retains no partial state.

---

### User Story 2 — Operator creates, updates, and removes blueprints at runtime through a mutable source (Priority: P1)

The host composes a configuration-file provider (read-only) with a database-backed provider
(read/write). The database provider implements the blueprint manager contract; the
configuration provider does not. An operator uses a management API to create a new blueprint
`acme-43`. The create call routes to the database provider's manager, which writes the
blueprint to persistent storage. A subsequent update and delete for `acme-43` likewise route
to the manager; delete additionally drains the active generation if one exists. Attempting
to create, update, or delete `built-in-config-shell` (which lives in the configuration
provider) fails with a clear error.

**Why this priority**: Runtime mutation is the second motivating requirement and is only
usable in conjunction with Story 1. Without a clean separation between source-of-truth and
in-memory state, write flows silently corrupt either the persistent catalogue or the live
registry.

**Independent Test**: Compose a stub read-only provider and a stub read/write provider.
Invoke create/update/delete through the registry's manager lookup. Assert (a) mutations on
read-only-owned names raise `BlueprintNotMutableException`; (b) mutations on read/write-owned
names invoke the correct manager method; (c) delete additionally drains any active
generation.

**Acceptance Scenarios**:

1. **Given** a composite of a read-only provider and a read/write provider, **When**
   `GetManager("acme-43")` is called for a name the read/write provider claims, **Then** the
   read/write provider's manager is returned.
2. **Given** the same composite, **When** `GetManager("built-in-config-shell")` is called
   for a name only the read-only provider claims, **Then** `GetManager` returns `null`.
3. **Given** a manager exists for name `acme-43`, **When** the operator calls
   `UnregisterBlueprintAsync("acme-43")`, **Then** the manager's `DeleteAsync` is invoked
   first, and only after it succeeds does the registry drain any active generation and
   remove its in-memory index entry.
4. **Given** no manager exists for a name, **When** the operator calls
   `UnregisterBlueprintAsync`, **Then** the call fails with `BlueprintNotMutableException`
   and no runtime state changes.
5. **Given** the manager's `DeleteAsync` fails, **When** `UnregisterBlueprintAsync` runs,
   **Then** the failure propagates, the in-memory index is unchanged, and any active
   generation is still serving requests.
6. **Given** a manager's `CreateAsync` succeeds for name `acme-44`, **When** a subsequent
   `GetOrActivateAsync("acme-44")` runs, **Then** the provider returns the newly-created
   blueprint and the shell activates as generation 1.

---

### User Story 3 — Host composes multiple blueprint sources with deterministic precedence (Priority: P2)

A host registers three providers: an in-memory provider (populated by `AddShell(...)` calls
in composition root), a configuration provider (reading `Shells/*.json`), and a storage
provider (reading a blob container). The registry treats them as a single logical catalogue.
Blueprint lookups probe providers in a defined order and return the first match. If two
providers claim the same name, host startup fails with a clear error naming the conflict.

**Why this priority**: Real deployments mix immutable baseline shells (code and configuration)
with dynamic tenant shells (storage). Without deterministic composition, lookup results
depend on registration order alone, which makes shell provenance non-obvious and hides
accidental shadowing.

**Independent Test**: Register three stub providers that each claim a distinct set of names,
assert lookup for each name routes to the owning provider. Register two providers that claim
overlapping names and assert the duplicate is detected and reported.

**Acceptance Scenarios**:

1. **Given** three providers each claiming disjoint names, **When** `GetBlueprintAsync` is
   called for a name owned by the second provider, **Then** the first provider is consulted
   first (returns `null`), the second returns a hit, and the third is not consulted.
2. **Given** two providers both returning a non-null blueprint for the same name, **When**
   any lookup occurs that would expose the conflict, **Then** the host raises a
   `DuplicateBlueprintException` identifying both provider types and the conflicting name.
3. **Given** a composite of N providers, **When** `ListAsync` pages through the combined
   catalogue, **Then** every blueprint from every provider appears exactly once across the
   pages, and each page's cursor is an opaque string that resumes iteration correctly.

---

### User Story 4 — Request-driven activation integrates with ASP.NET Core routing (Priority: P2)

An ASP.NET Core host uses the `CShells.AspNetCore` middleware. When a request arrives whose
resolved shell name has no active generation, the middleware calls `GetOrActivateAsync`
before dispatching to the shell's pipeline. If the blueprint does not exist, the middleware
responds `404`. If the provider is temporarily unavailable, the middleware responds `503`.
Concurrent requests for the same inactive shell are serialized on activation and then served
in parallel.

**Why this priority**: Without this, scale-ready activation is theoretically present but not
wired to the primary entry point host developers use. This story makes the P1 capability
observable from a real HTTP flow.

**Independent Test**: Start a minimal ASP.NET Core host with a stub provider, issue a request
for a never-activated name, assert the request completes successfully and the shell is now
active. Issue a request for a name the provider does not know about, assert `404`. Issue
requests for a name whose lookup throws, assert `503`.

**Acceptance Scenarios**:

1. **Given** a request whose resolved shell name has no active generation, **When** the
   middleware runs, **Then** it calls `GetOrActivateAsync`, waits for activation to complete,
   and dispatches the request against the newly-active shell.
2. **Given** the provider returns `null` for the resolved name, **When** the middleware runs,
   **Then** it responds `404 Not Found`.
3. **Given** the provider's lookup throws `ShellBlueprintUnavailableException`, **When** the
   middleware runs, **Then** it responds `503 Service Unavailable` and the registry holds no
   partial state for the name.

---

### User Story 5 — Operator pages through the full blueprint catalogue (Priority: P3)

An operator using a future admin API (feature `009`) needs to list every blueprint
regardless of whether its shell is active. The registry's list endpoint returns pages of
bounded size, each page including name, owning provider identifier, and whether the
blueprint is mutable (i.e., has a manager). The operator receives an opaque cursor in each
response and uses it to request the next page until the cursor is absent.

**Why this priority**: P3 because no consumer ships in this feature (the admin API is a later
feature), but locking in the contract now prevents a breaking redesign later.

**Independent Test**: Register a provider with 1 000 stub blueprints, call `ListAsync` with
`Limit = 100`, assert pagination yields exactly 10 pages of 100 entries each, every name
appears exactly once, and pages are returned in a stable order across repeated runs when the
catalogue is unchanged.

**Acceptance Scenarios**:

1. **Given** a catalogue of 1 000 blueprints across two providers, **When** the operator
   pages through with `Limit = 100`, **Then** exactly 1 000 unique entries are returned
   across 10 pages and the final page's `NextCursor` is `null`.
2. **Given** a page's `NextCursor`, **When** it is passed back in a subsequent
   `BlueprintListQuery`, **Then** the response begins immediately after the last entry of
   the previous page with no gaps or duplicates.
3. **Given** a `NamePrefix` filter, **When** the operator pages through results, **Then**
   only names matching the prefix are returned.

---

### Edge Cases

- **Activation stampede**: 1 000 concurrent requests for a never-activated name must result
  in the provider being queried exactly once and the shell being built exactly once
  (serialized per name, as with `ReloadAsync` in feature `006`).
- **Provider unavailability mid-lookup**: when `GetOrActivateAsync` fails because the
  provider throws, the registry must retain no partial state for that name; the next call
  must re-attempt the lookup.
- **Manager write succeeds, drain fails**: when `UnregisterBlueprintAsync` successfully
  deletes from the underlying store but the subsequent drain raises, the persistent state
  is already gone; the in-memory registry entry is still force-removed so the catalogue
  and registry remain consistent at the next activation attempt.
- **Duplicate name across providers**: detected either at composite registration or at first
  collision-observable lookup (listing, or a lookup consulting multiple providers); raises
  `DuplicateBlueprintException` rather than silently shadowing.
- **Unregister during in-flight activation**: if activation is in progress for name `X` and
  `UnregisterBlueprintAsync("X")` is called, the unregister serializes against the
  activation's per-name lock. The completed activation may briefly be active before it is
  drained.
- **Pagination under concurrent catalogue mutation**: if a blueprint is created, updated, or
  deleted while paging is in progress, pages may skip or duplicate entries. This is
  acceptable; callers are expected to tolerate catalogue churn during iteration.
- **Empty catalogue**: `ListAsync` on a host with no providers or no blueprints returns an
  empty page with `NextCursor = null`.

## Requirements *(mandatory)*

### Functional Requirements

**Blueprint provider contract**

- **FR-001**: A blueprint provider MUST expose an on-demand lookup operation that returns
  the blueprint for a given name, or `null` when the provider does not claim that name.
- **FR-002**: A blueprint provider MUST expose a paginated listing operation whose caller
  supplies a query (cursor, limit, optional name prefix) and receives a page plus an opaque
  `NextCursor` string (or `null` on the final page).
- **FR-003**: A blueprint provider MUST NOT be required to enumerate its entire catalogue at
  any point in normal operation (startup, activation, routing). Enumeration is only
  required when explicitly paged through via `ListAsync`.
- **FR-004**: A blueprint provider MAY implement `ExistsAsync(name)` as a cheaper
  alternative to a full lookup; the default behavior is equivalent to
  `GetAsync(name) is not null`.

**Blueprint manager contract**

- **FR-005**: A blueprint manager MUST expose `CreateAsync`, `UpdateAsync`, and
  `DeleteAsync` operations taking a `ShellSettings` (create/update) or name (delete) and
  returning when the underlying store has committed the change.
- **FR-006**: A blueprint manager MUST expose an `Owns(name)` predicate that the registry
  uses to route write operations to the correct manager in a composite arrangement.
- **FR-007**: A blueprint manager MUST persist changes to its underlying store before
  returning success from any mutating operation.
- **FR-008**: A blueprint manager's `DeleteAsync` MUST NOT touch the registry's in-memory
  state directly — runtime cleanup (draining the active generation, removing the index
  entry) is the registry's responsibility.

**Provider/manager pairing**

- **FR-009**: A provider that vends a given name MUST be able to indicate the presence of a
  manager for that name via the `ProvidedBlueprint` record it returns from its lookup.
- **FR-010**: A provider and its manager MAY be implemented by the same type or by two
  separate types, at the host's discretion.
- **FR-011**: Read-only providers (no manager association) MUST cause mutation attempts on
  their owned names to fail with `BlueprintNotMutableException`.

**Composite provider**

- **FR-012**: A composite provider MUST preserve registration order for lookup, returning
  the first non-`null` hit and short-circuiting subsequent probes.
- **FR-013**: A composite provider MUST merge paginated listings across its constituent
  providers, exposing an opaque composite cursor that encodes each sub-provider's
  independent cursor.
- **FR-014**: A composite provider MUST detect duplicate names across its constituents and
  raise `DuplicateBlueprintException` identifying both offending provider types and the
  conflicting name. Detection MAY occur lazily (at first colliding lookup or list) rather
  than at composition time.

**Registry**

- **FR-015**: The registry MUST expose `GetOrActivateAsync(name)` which returns the active
  generation if present; otherwise performs a provider lookup and, on a hit, builds a new
  generation, runs initializers, promotes to `Active`, and returns it.
- **FR-016**: `GetOrActivateAsync` for the same name MUST be serialized — concurrent calls
  for an inactive name result in exactly one provider lookup, one shell build, and a
  single shell instance observed by all callers.
- **FR-017**: `GetOrActivateAsync` MUST raise `ShellBlueprintNotFoundException` when the
  provider returns `null` for the requested name, and `ShellBlueprintUnavailableException`
  when the provider's lookup throws (wrapping the original exception).
- **FR-018**: The registry MUST expose `GetBlueprintAsync(name)` that delegates to the
  provider without activating a shell. Used by admin-facing read flows.
- **FR-019**: The registry MUST expose `GetManager(name)` that returns the manager
  associated with the owning provider, or `null` when none exists.
- **FR-020**: The registry MUST expose `UnregisterBlueprintAsync(name)` that, in order:
  invokes `GetManager(name)?.DeleteAsync(name)`, drains the active generation if any, and
  removes the in-memory index entry. Absence of a manager raises
  `BlueprintNotMutableException` without touching runtime state.
- **FR-021**: The registry MUST expose `ListAsync(ShellListQuery)` that pages through the
  provider's catalogue and left-joins the in-memory lifecycle state (active generation,
  lifecycle state, last-scope timestamp, active scope count, mutability) onto each entry.
- **FR-022**: The registry MUST expose `ReloadActiveAsync(ReloadOptions)` replacing the
  prior `ReloadAllAsync`. Reloads ONLY shells currently active in the registry, bounded by
  a configurable `MaxDegreeOfParallelism` (default 8).
- **FR-023**: The registry MUST remove the operations `RegisterBlueprint`, `GetBlueprint`,
  `GetBlueprintNames`, and `ReloadAllAsync`. No compatibility shim is provided.
- **FR-024**: Existing registry operations (`ActivateAsync`, `ReloadAsync`, `DrainAsync`,
  `GetActive`, `GetAll`, `Subscribe`, `Unsubscribe`) MUST continue to behave as specified
  by feature `006`, with the exception that `ActivateAsync` and `ReloadAsync` now consult
  the provider for blueprint lookup rather than an internal blueprint dictionary.

**Builder and hosting**

- **FR-025**: `builder.Services.AddCShells(c => c.AddShell("name", ...))` MUST continue to
  work and MUST register the resulting blueprint with a built-in in-memory provider.
- **FR-026**: The host's startup hosted service MUST NOT enumerate the catalogue at
  startup. If a host wishes to pre-warm specific shells, it MUST do so explicitly (e.g.,
  by calling `GetOrActivateAsync` for chosen names).
- **FR-027**: The ASP.NET Core routing middleware MUST call `GetOrActivateAsync` for the
  resolved shell name and translate its exceptions to HTTP responses:
  `ShellBlueprintNotFoundException` → `404`, `ShellBlueprintUnavailableException` → `503`.

**Migration of existing providers**

- **FR-028**: `ConfigurationShellBlueprintProvider` MUST be migrated to the new contract.
  It remains read-only (no manager).
- **FR-029**: The `CShells.Providers.FluentStorage` provider MUST implement both provider
  and manager contracts, replacing its current sync-over-async startup wiring with
  on-demand async reads.

### Key Entities

- **`IShellBlueprintProvider`**: Source-agnostic catalogue. Lookup + paginated list.
  Returns `ProvidedBlueprint` records. Registered per-source in DI; composed by the
  built-in composite.
- **`IShellBlueprintManager`**: Optional write-side peer of a provider. Owns a subset of
  names declared via `Owns(name)`. Handles create/update/delete against the underlying
  store. Registered independently in DI; the registry discovers it via the composite's
  provider hit.
- **`ProvidedBlueprint`**: Record returned by `IShellBlueprintProvider.GetAsync` pairing
  the blueprint with an optional manager reference.
- **`BlueprintListQuery`** / **`BlueprintPage`** / **`BlueprintSummary`**: Pagination DTOs
  for catalogue listing. `BlueprintSummary` carries name, owning provider identifier, and
  mutability flag.
- **`ShellListQuery`** / **`ShellPage`** / **`ShellSummary`**: Pagination DTOs for the
  registry's left-joined view. `ShellSummary` extends `BlueprintSummary` with active
  generation, lifecycle state, and active scope count.
- **`ReloadOptions`**: Configuration for `ReloadActiveAsync`. Currently:
  `MaxDegreeOfParallelism`.

### Assumptions

- Host developers using `AddShell(...)` in composition root do not observe any API change;
  the builder routes through an internal in-memory provider.
- Pagination cursors are opaque base64 strings. They are not authenticated (no HMAC); the
  admin API is assumed to be behind host-level authorization.
- Default `MaxDegreeOfParallelism` for `ReloadActiveAsync` is 8; host-tunable via
  `ReloadOptions`.
- Duplicate name detection between providers may surface at first colliding operation
  rather than at composition time — sources may be async/expensive to enumerate, so the
  composite does not pre-scan.
- A host with no registered providers is legal; `ListAsync` returns empty pages and every
  activation attempt raises `ShellBlueprintNotFoundException`.
- The first page of a listing may be requested with `Cursor = null`; providers define
  their own cursor encodings.
- Pagination is not guaranteed stable across catalogue mutation; callers must tolerate
  skip/duplicate during iteration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Host startup time with a provider claiming 100 000 blueprints is
  indistinguishable (within 50 ms) from startup time with a provider claiming 10
  blueprints. The catalogue is not enumerated at startup.
- **SC-002**: When 1 000 concurrent requests arrive for a never-activated shell, the
  underlying provider's lookup operation is invoked exactly once and the shell is built
  exactly once.
- **SC-003**: Listing 100 000 blueprints with `Limit = 100` completes in at most 1 000
  page requests and visits each blueprint exactly once, regardless of which providers own
  the names.
- **SC-004**: A delete request for a blueprint whose source is read-only returns a
  structured `BlueprintNotMutableException` (or HTTP `409` via future admin API) within
  10 ms, without touching runtime state.
- **SC-005**: After `UnregisterBlueprintAsync("X")` completes successfully, (a) the
  underlying store no longer contains the blueprint, (b) the active generation (if any)
  is in `Disposed` state, and (c) a subsequent `GetOrActivateAsync("X")` raises
  `ShellBlueprintNotFoundException`.
- **SC-006**: Every existing feature `006` test scenario (activation, reload, drain,
  scope tracking, state transitions) continues to pass after migration, demonstrating
  that the refactor preserves drain-lifecycle behavior unchanged.
- **SC-007**: The `CShells.Providers.FluentStorage` provider no longer calls
  `GetAwaiter().GetResult()` during service registration.