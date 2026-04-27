# Feature Specification: Shell Management REST API

**Feature Branch**: `009-management-api`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Ship a new optional class library, CShells.Management.Api, that exposes a small set of root-level Minimal API endpoints onto an existing IEndpointRouteBuilder. Primary purpose: make manual testing and demonstration of shell reload and drain-lifecycle mechanics easy from outside the running process. The host installs the endpoints with one line and applies its own authorization, CORS, rate limiting, etc. by chaining the standard ASP.NET Core endpoint conventions on the returned RouteGroupBuilder. Endpoint surface: list shells (paginated), get one shell with all generations and per-generation drain snapshots, get one shell's blueprint without activating, reload one shell, reload all active shells (with optional maxDegreeOfParallelism query param), force-drain the in-flight drain on a shell. The package depends only on CShells.Abstractions plus the Microsoft.AspNetCore.App framework reference. It deliberately does NOT depend on CShells.AspNetCore. Multi-target net8.0;net9.0;net10.0. Authentication is the host's responsibility. To make per-generation drain observability work, the framework adds a small abstraction extension: each non-active IShell exposes its in-flight IDrainOperation directly (the registry already keeps a single drain instance per draining generation internally)."

## Clarifications

### Session 2026-04-27

- Q: Should blueprint response payloads (`GET /{name}` and `GET /{name}/blueprint`) include the registered `ConfigurationData` map, redact it, omit it, or expose it through an opt-in? → A: **Include `ConfigurationData` verbatim** — consistent with the package's "authorization is the host's responsibility" stance (FR-014). Faithfully mirroring the registry preserves manual-testing fidelity (e.g., verifying that an out-of-band edit to the configuration source landed before triggering a reload).
- Q: When multiple in-flight drains coexist for the same shell name (i.e., consecutive reloads left more than one previous generation still draining), which generation(s) does `POST /{name}/force-drain` target? → A: **Force every in-flight (`Deactivating` / `Draining`) generation for that shell name.** `Drained` generations are skipped (already terminal). The response is an **array** of per-generation drain results — one entry per forced generation — so callers still see per-generation outcomes.
- Q: Does the force-drain endpoint await each drain to a terminal state before responding, or return immediately after triggering the force? → A: **Await each drain to a terminal state, then return the array of final `DrainResult` entries.** Response time tracks the longest grace period across forced drains. The headline manual-testing UX — single-request, structured outcome — is preserved; hosts that want shorter blocking configure a shorter grace policy.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reload a single shell over HTTP and observe the result (Priority: P1)

A developer working on a CShells host wants to validate that a particular
shell reloads cleanly without restarting the process. They send a single HTTP
request to the management endpoint with the shell's name and observe the
outcome — the new generation, its lifecycle state, and a snapshot of the
drain operation kicked off on the previous generation — in the response body.

This is the headline scenario. The whole module exists so this kind of
out-of-process probe takes one line of host setup and one HTTP request.

**Why this priority**: Without this, the package has no reason to exist. Every
other capability (list, force-drain, batch reload) is an extension of, or
support for, "I want to reload one shell and see what happened."

**Independent Test**: Build a host with one in-memory shell named `acme`,
install the management endpoints under `/admin`, send `POST /admin/reload/acme`,
assert the response describes a successful reload with the next generation
number and a non-null drain snapshot for the previous generation.

**Acceptance Scenarios**:

1. **Given** a host with the management endpoints installed and a shell named
   `acme` already activated at least once, **When** a developer issues
   `POST /admin/reload/acme`, **Then** the response is a success status with a
   structured body containing the shell name, a flag indicating success, the
   newly-activated generation's identifying number, the new generation's
   lifecycle state, and a non-null drain snapshot describing the in-flight
   drain on the previous generation.
2. **Given** the same host, **When** a developer issues a reload for a name
   that is not registered with any blueprint provider, **Then** the response
   is a not-found status with a problem-details body that names the missing
   shell.
3. **Given** a host whose blueprint provider throws when consulted, **When**
   a developer issues a reload, **Then** the response is a service-unavailable
   status with a problem-details body explaining that the source was
   unreachable, and the shell remains in its prior state (no torn-down
   generation).

---

### User Story 2 - Reload every active shell from a single request (Priority: P1)

A developer wants to sanity-check that all currently-active shells survive a
reload — a common pre-deployment smoke test. They send one HTTP request to
the batch-reload endpoint and receive an array of per-shell outcomes,
successes and failures both included. The batch never aborts midway just
because one shell fails: every active shell is attempted, and the response
tells the developer per-shell what happened.

**Why this priority**: The "reload everything and see what breaks" workflow
is the single most useful operation for shaking out drain-handler bugs across
a multi-shell deployment. P1 because partial-failure reporting is a hard
requirement of this story — the developer has to be able to see which shells
broke and which did not, in one response.

**Independent Test**: Build a host with three active shells, send the batch
reload, assert the response contains exactly three per-shell entries each
indicating success and an advanced generation number.

**Acceptance Scenarios**:

1. **Given** a host with three active shells, **When** a developer issues a
   batch reload, **Then** the response contains exactly three per-shell
   entries, each indicating success and reporting an advanced generation
   number.
2. **Given** a host where exactly one shell's blueprint source fails on
   lookup, **When** a developer issues a batch reload, **Then** the response
   status is still success overall, the failing shell's entry is marked
   unsuccessful with an `error` field carrying the source's message, and
   every other shell's entry is successful.
3. **Given** a host with no active shells (empty registry or all blueprints
   inactive), **When** a developer issues a batch reload, **Then** the
   response is a successful empty array — consistent with the framework's
   lazy-activation model where inactive blueprints stay inactive.

---

### User Story 3 - Inspect shells and per-generation drain state in real time (Priority: P1)

A developer triggers a reload and immediately wants to watch the previous
generation move through `Deactivating → Draining → Drained → Disposed`.
They poll a "get one shell" endpoint and observe the response shrinking from
two listed generations (the new active one plus the previous one still
draining) down to one (only the new active generation, after the previous
finished drain and was released). For each non-active generation in the
response, they see the drain operation's current status and deadline so they
can tell whether the drain is in progress, completed, timed out, or forced.

A developer also wants to list every shell the host knows about — including
inactive blueprints that have never been touched — and confirm a freshly
edited blueprint source picked up the change without activating it. Two
read endpoints support this: a paginated list of all known blueprints joined
with their active-generation lifecycle state, and a single "fetch the
registered blueprint without activating" endpoint.

**Why this priority**: The whole motivation of this module is **watching**
the drain mechanics. Without per-generation read endpoints — and specifically
without each generation's drain status surfaced inline — the developer can
only see "before" and "after" snapshots from the reload-response payloads.
They cannot watch the drain progress in flight. The read endpoints close
that loop and are P1 alongside reload itself.

**Independent Test**: Reload a shell, then poll the get-one endpoint at
short intervals; observe the response transitioning from two generations to
one; for the still-draining generation observe its drain-status field
transition from "Pending" to "Completed" (or "TimedOut"/"Forced") before the
generation disappears from the response.

**Acceptance Scenarios**:

1. **Given** a host with five blueprints (three active, two inactive),
   **When** a developer requests the catalogue list, **Then** the response is
   a paginated page with five entries, each entry naming the shell, its
   registered features, and (for the three active) its current
   active-generation number and lifecycle state.
2. **Given** a host where a reload of `acme` has just been issued and the
   previous generation is still draining, **When** a developer requests the
   focused view for `acme`, **Then** the response contains an array with two
   generation entries — the new active generation and the previous draining
   generation — and the previous-generation entry carries a non-null drain
   snapshot whose `status` is one of the in-flight or terminal drain
   statuses.
3. **Given** the same host, **When** a developer polls the focused view
   repeatedly, **Then** the response eventually transitions from a
   two-generation list to a one-generation list as the previous generation
   completes drain and is disposed, and at that point no drain snapshot
   appears for the remaining (active) generation.
4. **Given** a host where blueprint `tenant-x` is registered but has never
   been activated, **When** a developer requests the focused view for
   `tenant-x`, **Then** the response carries the registered blueprint
   information but reports zero generations.
5. **Given** a host where blueprint `tenant-y` is unknown to every blueprint
   source, **When** a developer requests the focused view for `tenant-y`,
   **Then** the response is a not-found status.
6. **Given** a host where `tenant-z` is registered but inactive, **When** a
   developer requests `tenant-z`'s blueprint via the dedicated blueprint
   endpoint, **Then** the response carries the blueprint and the host
   performs no activation as a side effect.

---

### User Story 4 - Force every in-flight drain on a shell to complete immediately (Priority: P1)

A developer is testing drain handlers and has a shell with one or more
generations stuck in `Deactivating` or `Draining` because handlers are
intentionally slow (or hung). They want to force **every** in-flight drain
for that shell name to terminate now — the same effect as calling
`IDrainOperation.ForceAsync()` on each in-flight drain from in-process code
— without restarting the host. They send a single HTTP request and the
framework cancels outstanding handler tokens on each in-flight generation,
transitions each through `Drained` after the configured grace, and returns
an array of per-generation drain results.

For this to be a real first-class endpoint (not a workaround using lifecycle
events or background-recorded state), the framework surfaces each non-active
generation's in-flight `IDrainOperation` directly on `IShell`. This is the
"drain observability extension" that goes hand-in-hand with this feature:
the registry already keeps exactly one drain instance per draining
generation internally (per the `IDrainOperation` contract: "concurrent
callers for the same shell receive the same instance"), so this story
exposes what the registry already tracks, and does not add new state. The
endpoint walks every generation currently held for the shell name (per
`IShellRegistry.GetAll(name)`), filters to those whose lifecycle state is
`Deactivating` or `Draining`, retrieves each generation's drain via
`IShell.Drain`, and forces them all.

**Why this priority**: Manual testing of drain semantics is **the** stated
motivation for this whole module. Without a force-drain endpoint, a
developer poking at drain mechanics from outside the process has no way to
unstick a slow drain except killing the process. P1 because the
motivation justifies it and because the abstraction extension that supports
it (per-generation drain on `IShell`) also unlocks the per-generation drain
snapshots that Story 3 depends on.

**Independent Test**: Register a deliberately-slow drain handler on a shell,
trigger two reloads back-to-back so two previous generations are draining,
confirm via the focused view that both previous generations are in
`Draining` with drain status `Pending`, then send the force-drain request,
and confirm the response carries a two-element array of drain results both
with status `Forced` (or `Completed`), and that subsequent polls of the
focused view no longer list the forced generations.

**Acceptance Scenarios**:

1. **Given** a host where shell `acme` has exactly one previous generation
   still draining, **When** a developer issues
   `POST /admin/acme/force-drain`, **Then** the response is a successful
   status carrying a one-element array whose only entry is a drain-result
   for the forced generation, with status `Forced` (or `Completed` if the
   drain finished naturally between request arrival and forcing).
2. **Given** a host where shell `acme` has two previous generations both
   still draining (e.g., from consecutive reloads), **When** a developer
   issues a force-drain on `acme`, **Then** the response is a successful
   status carrying a two-element array of drain results — one per
   forced generation — and each entry's `status` is `Forced` or
   `Completed`.
3. **Given** a host where shell `acme` has no in-flight drain (no
   `Deactivating`/`Draining` generation; only the active generation
   exists, or only `Drained` non-active generations remain), **When** a
   developer issues a force-drain on `acme`, **Then** the response is a
   not-found status with a problem-details body explaining there is no
   in-flight drain to force.
4. **Given** the same host, **When** a developer issues a force-drain for
   an unknown shell name, **Then** the response is a not-found status.
5. **Given** any non-active generation surfaced by the focused view of a
   shell, **When** a developer reads the response, **Then** the
   per-generation entry carries a non-null drain snapshot describing that
   generation's in-flight or terminal drain status. (This codifies the
   per-generation drain-on-`IShell` extension as part of the user-observable
   contract, not just an internal aid for the force-drain endpoint.)

---

### User Story 5 - Wrap the endpoints behind the host's existing authorization (Priority: P1)

A developer ships the management endpoints behind their host's existing
auth scheme. Because the install method returns a standard
ASP.NET Core endpoint-convention builder (specifically a route group),
the developer chains `RequireAuthorization`, `RequireCors`,
`RequireRateLimiting`, `WithTags`, `WithOpenApi`, `AddEndpointFilter`, etc.
on the result. Without authorization, the endpoints expose direct control
over the running registry — a foot-gun in any non-local environment — so
**composing with stock ASP.NET Core authorization is a hard requirement**
of the install surface.

**Why this priority**: P1 because shipping a management surface that cannot
be authorized would make the package unsafe to use anywhere except a single
developer's laptop. The fact that authorization is the host's
responsibility (rather than a baked-in policy) is fine — the install seam
just has to compose cleanly with the standard ASP.NET Core conventions.

**Independent Test**: Install the endpoints, chain a default-deny
authorization policy, hit any endpoint without a credential and observe a
401; hit it with a valid credential and observe the same response a plain
install would return.

**Acceptance Scenarios**:

1. **Given** a host whose install line chains a default-deny authorization
   requirement, **When** an unauthenticated client hits any management
   endpoint, **Then** the response is unauthorized and the registry is
   never consulted.
2. **Given** the same host, **When** an authenticated and authorized client
   hits any management endpoint, **Then** the response matches what an
   unprotected install would have returned for the same request.
3. **Given** a host whose install line attaches a custom audit
   endpoint-filter, **When** any management endpoint is invoked, **Then**
   the audit filter runs before the endpoint handler executes.

---

### User Story 6 - Override reload-all parallelism via query string (Priority: P2)

A developer wants to reproduce a contention bug that surfaces only at low
parallelism. They issue the batch reload with a query parameter forcing
serial reload (one shell at a time). The framework validates the value
against the existing reload-options range and returns a clear
bad-request error if it falls outside.

**Why this priority**: P2 because the default parallelism is correct for
nearly every test scenario. The override exists for targeted reproductions
and demos.

**Independent Test**: Issue the batch reload with the parallelism query set
to `1`; observe success. Issue with `0` and with `65`; observe a
bad-request response naming the parameter and the allowed range. Issue
without the parameter; observe the framework's default parallelism applied.

**Acceptance Scenarios**:

1. **Given** a host with three active shells, **When** a developer issues
   the batch reload with the parallelism query set to `1`, **Then** the
   response is successful and the underlying registry call is observed to
   use the requested parallelism.
2. **Given** the same host, **When** the parallelism query is `0`, `65`, or
   a non-integer, **Then** the response is a bad-request status with a
   problem-details body naming the parameter and the allowed inclusive
   range.
3. **Given** the same host, **When** the parallelism query is omitted,
   **Then** the framework's default parallelism is applied.

---

### User Story 7 - Sample host (Workbench) wires the API end-to-end (Priority: P2)

The shipped Workbench sample wires the management endpoints (unprotected,
sample-only, with a code comment marking it as such) so a developer can
`curl` the running sample to play with reload + drain mechanics without
writing any code. The sample's README gains a "Manual Testing via the
Management API" section listing one example call each for reload-single,
reload-all, list, and force-drain.

**Why this priority**: P2 because the sample is documentation, not core
functionality. But the manual-testing motivation makes a working sample
valuable enough to include — it is the five-minute on-ramp to the module.

**Independent Test**: Run the Workbench sample, issue `POST` calls to the
documented endpoints with a plain HTTP client, and confirm the documented
behavior is observed end-to-end.

**Acceptance Scenarios**:

1. **Given** the Workbench sample running locally, **When** a developer
   sends a batch reload request, **Then** every Workbench shell is reloaded
   and the response describes the per-shell outcomes.
2. **Given** the Workbench README, **When** a developer reads the "Manual
   Testing via the Management API" section, **Then** the section names the
   install line and at least one example call for each of: reload-single,
   reload-all, list, force-drain.
3. **Given** the Workbench `Program.cs`, **When** a developer reads the
   install site, **Then** the code carries an explicit comment that the
   install is unprotected and is for sample / local-development purposes
   only.

---

### Edge Cases

- **Empty registry**: list returns an empty page; reload-all returns an
  empty array; reload-one and the focused view both return not-found.
- **Concurrent reloads of the same shell**: the framework's per-name
  serialization (the registry's per-name semaphore) makes concurrent
  reload requests for the same name return ordered, deterministic
  outcomes. Neither request is rejected and neither sees a transient
  inconsistent state.
- **Reload during host shutdown**: the framework's shutdown coordinator may
  cancel in-flight reloads. The endpoint surfaces the cancellation as a
  service-unavailable response with a problem-details body naming
  shutdown as the cause.
- **Drain still in flight when the response is built**: the reload response's
  drain snapshot is captured at the moment the response is serialized — it
  reflects the drain's current status / deadline, not a "complete" terminal
  state. Developers observing drain progress in flight should poll the
  focused-view endpoint rather than expect the reload response to block
  until drain finishes. The focused view's per-generation drain field is
  the canonical observability surface.
- **Force-drain on a shell whose only non-active generations are
  already `Drained`**: those generations are skipped (their drains are
  already terminal); since the array of forced generations would be
  empty, the endpoint returns a not-found status with a problem-details
  body explaining there is no in-flight drain to force.
- **Force-drain race with naturally-completing drain**: if a drain
  transitions to `Completed` between the registry walk and the
  `ForceAsync` call, the framework treats the call as a no-op and the
  per-generation result reports the terminal status (`Completed`) rather
  than `Forced`. This is benign and visible in the response.
- **Reload-all when one or more shells fail**: the batch never aborts. The
  overall response status remains successful; per-entry failures are
  carried in each entry's `error` field.

## Requirements *(mandatory)*

### Functional Requirements

**Endpoint surface and install seam**

- **FR-001**: The framework MUST ship a new optional class library that
  exposes a single public install method on the standard
  endpoint-route-builder type. The method MUST accept a route prefix
  (defaulting to `/_admin/shells`) and MUST return the standard
  endpoint-convention-builder type that the host can chain authorization,
  CORS, rate-limiting, OpenAPI, endpoint-filter, and tagging conventions
  on.
- **FR-002**: Under the configured prefix, the install method MUST register
  exactly the following routes and no others:
  1. List blueprints catalogue (paginated; supports cursor and page-size
     query parameters).
  2. Focused view for a single named shell (registered blueprint + every
     still-held generation, each generation's lifecycle state, and each
     non-active generation's in-flight drain snapshot).
  3. Fetch one shell's registered blueprint without activating.
  4. Reload one named shell.
  5. Reload every active shell, accepting an optional parallelism query
     parameter.
  6. Force-drain the in-flight drain on the most recent non-active
     generation of a named shell.
- **FR-003**: The package MUST NOT register any service in DI. Hosts wire
  the package by calling exactly one method on the endpoint-route-builder
  type. The endpoints resolve the registry from the root service provider
  the framework's existing setup already populated.

**Drain observability — abstraction extension**

- **FR-004**: The framework's `IShell` abstraction MUST expose, on every
  shell instance, the in-flight drain operation associated with that
  generation. The value MUST be non-null exactly when the generation's
  lifecycle state is `Deactivating`, `Draining`, or `Drained`, and MUST be
  null when the state is `Initializing`, `Active`, or `Disposed`. The
  exposed reference MUST be the same instance the registry has been
  returning all along from `DrainAsync` and `ReloadAsync` for that
  generation — not a new tracker, snapshot, or proxy. This requirement
  exists in `CShells.Abstractions` (and its `CShells` implementation), not
  in the management API package.
- **FR-005**: The management API's focused-view endpoint MUST surface, for
  each non-active generation in its response, a JSON-serializable snapshot
  of that generation's in-flight drain — minimally `status` and
  `deadline` — produced from the value exposed by FR-004.
- **FR-006**: The management API's force-drain endpoint MUST enumerate
  every generation currently held for the requested shell name (via
  `IShellRegistry.GetAll(name)`), filter to those whose lifecycle state is
  `Deactivating` or `Draining` (the in-flight drains; `Drained`
  generations are skipped because they are already terminal), retrieve
  each one's in-flight drain via FR-004, invoke the framework's existing
  force-drain mechanism on each, await each drain to a terminal state, and
  return an **array** of drain-result entries — one per forced generation,
  in any order. If the shell name is unknown the response MUST be a
  not-found status. If the shell name is known but **no** generation is
  currently in `Deactivating` or `Draining` (i.e., the array would be
  empty), the response MUST also be a not-found status with a
  problem-details body explaining there is no in-flight drain to force —
  this distinguishes "no drain to force" from "successfully forced zero
  generations."

**Reload outcomes and parallelism**

- **FR-007**: Single-reload responses MUST carry a structured per-shell
  outcome containing minimally: the shell name, a success flag, the new
  generation's identifying number and lifecycle state when present, and the
  previous-generation drain snapshot (status + deadline) when present, or an
  error description when composition / build / initializer failed.
- **FR-008**: Batch-reload responses MUST carry an array of the same
  per-shell outcomes. The HTTP status remains successful even when
  individual entries are unsuccessful — the array is the authoritative
  per-shell signal.
- **FR-009**: The batch reload MUST accept an optional parallelism query
  parameter, validated against the existing reload-options range. When
  omitted, the framework's documented default parallelism applies. When
  out of range or non-integer, the response is a bad-request status with a
  problem-details body identifying the parameter and the allowed
  inclusive range.

**Read endpoints**

- **FR-010**: The list-catalogue endpoint MUST accept optional cursor and
  page-size query parameters and forward them into the framework's
  existing paginated list query. Defaults match the framework's existing
  defaults.
- **FR-011**: The focused-view endpoint MUST report, for the named shell:
  the registered blueprint (or null when no blueprint is registered), and
  the array of all generations currently held — including the active
  generation and any deactivating/draining/drained generations not yet
  disposed. Each generation entry MUST report its identifying number and
  lifecycle state, plus the drain snapshot from FR-004 / FR-005 when
  applicable.
- **FR-012**: The blueprint endpoint MUST return the registered blueprint
  for the named shell **without** triggering activation. Provider lookup
  failures (source unreachable) MUST surface as a service-unavailable
  status. Unknown names MUST surface as not-found.
- **FR-012a**: Blueprint response payloads (returned by both the
  focused-view endpoint per FR-011 and the dedicated blueprint endpoint
  per FR-012) MUST include the registered shell's `ConfigurationData` map
  verbatim, with no redaction or filtering applied by the package.
  Configuration values may contain host-controlled secrets; gating access
  to the management endpoints is the host's responsibility (FR-014). The
  XML doc-comment on the install method MUST mention configuration
  exposure as a specific reason production-style deployments must apply
  authorization.

**Error mapping**

- **FR-013**: Errors raised by the registry MUST be mapped to HTTP statuses
  consistently across all endpoints:

  | Condition                                              | Status                     |
  |--------------------------------------------------------|----------------------------|
  | Shell name has no blueprint                            | 404 Not Found              |
  | Blueprint source is unreachable                        | 503 Service Unavailable    |
  | Host shutdown cancels the operation                    | 503 Service Unavailable    |
  | Query-parameter out of range / wrong type              | 400 Bad Request            |
  | Force-drain on a name with no in-flight (`Deactivating`/`Draining`) generation | 404 Not Found |
  | Per-shell failure inside batch reload                  | Captured in entry's error field; batch HTTP status remains 200 |

  All non-2xx responses MUST use RFC 7807 problem-details bodies.

**Authentication**

- **FR-014**: The package MUST NOT apply any authorization, authentication,
  or rate-limit policy of its own. The XML doc-comment on the install
  method MUST state, in plain language, that the endpoints are unprotected
  by default and that production-style deployments must apply
  authorization.

**Documentation and sample**

- **FR-015**: The Workbench sample's program entry-point MUST wire the
  install method (unprotected — sample-only — flagged with an explicit
  code comment).
- **FR-016**: The Workbench sample's README MUST include a "Manual Testing
  via the Management API" section with at least one example HTTP call for
  each of: reload-single, reload-all, list, focused-view, force-drain.

**Out of scope**

- **FR-017**: The package and this feature MUST NOT include endpoints for:
  unregistering blueprints, mutating blueprints, websocket-based
  lifecycle event streaming, or framework-generated OpenAPI documents
  beyond what the host produces by chaining `WithOpenApi()` on the
  install method's return value. These are explicit non-goals of `009`.

### Key Entities

- **Management install method** — the single public surface of the new
  package. Returns the standard endpoint-convention-builder type so the
  host can chain any standard ASP.NET Core endpoint convention. Lives in
  the new package; is the only public type the package contributes.
- **Per-shell reload outcome** — the response shape for both single and
  batch reload. Carries the shell name, success flag, new generation
  number + state, previous-generation drain snapshot, and error
  description (when any).
- **Per-generation drain snapshot** — appears on each non-active
  generation in the focused-view response and on the previous-generation
  drain field in reload outcomes. Minimally contains the drain's status
  and deadline. Sourced from the `IShell.Drain` property added by
  FR-004.
- **`IShell.Drain` (framework abstraction extension)** — newly-added
  property on the framework's `IShell` abstraction. Exposes the in-flight
  drain operation for non-active generations directly — replacing the
  prior pattern where drain operations were observable only as transient
  return values from `ReloadAsync` / `DrainAsync`. Used by the focused-view
  and force-drain endpoints; available to any in-process caller as well.
- **Workbench sample install site** — the single line in the sample
  application that wires the management endpoints, with an explicit
  code-comment flagging it as unprotected and sample-only.

### Assumptions

- The host has already configured the framework (the equivalent of
  `AddCShells(...)`) so the registry is in DI. The package documents
  this prerequisite but does not enforce it; calling the install method
  in a host that has not registered the framework fails on the first
  request with the standard "no service registered" exception.
- Cross-shell management is intentionally a root concern. The endpoints
  run outside any shell's request scope; they resolve the registry from
  the root provider rather than from any shell's nested provider.
- The framework's existing per-name serialization in the registry is
  sufficient to keep concurrent management-endpoint calls deterministic.
  No additional locking, queueing, or rate-limiting is added by the
  package.
- Manual testing — not production telemetry — is the design target. The
  package emits no metrics or logs of its own beyond the standard
  request-pipeline logs already produced by the host.
- The shipped `IDrainOperation`'s "same instance for concurrent calls"
  contract means surfacing the in-flight drain on `IShell` (FR-004) is a
  visibility extension — it does not introduce new caching, lifetimes,
  or concurrency rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer who has installed a fresh CShells host can
  enable the management API by adding **one line** to their host's
  startup code — no service registration, no separate configuration file,
  no additional NuGet packages beyond the new optional package.
- **SC-002**: Once installed, the developer can reload a named shell with
  **one HTTP request** and observe a structured response describing the
  new generation, its lifecycle state, and the in-flight drain on the
  previous generation, with no further request needed to learn what
  happened.
- **SC-003**: A developer can reload every active shell in the host with
  **one HTTP request** and receive **per-shell outcomes** for every shell
  attempted; a failure for any single shell does not prevent the developer
  from learning about the others.
- **SC-004**: A developer polling the focused-view endpoint during an
  in-flight reload observes the previous generation's lifecycle state
  transitioning through `Deactivating`, `Draining`, and `Drained` (or
  `Forced` / `TimedOut`) before the generation disappears from the
  response — i.e., the endpoint exposes drain progress in real time, not
  just a before/after snapshot.
- **SC-005**: A developer can force every in-flight drain on a shell to
  terminate via **one HTTP request** and receive an array of drain
  results — one entry per forced generation — inline, without restarting
  the host.
- **SC-006**: A developer who chains a default-deny authorization
  requirement on the install method observes that **every** management
  endpoint becomes unauthorized to anonymous callers and authorized to
  approved callers, with no package-specific glue.
- **SC-007**: A developer who passes an out-of-range parallelism value to
  the batch reload receives a clear, parameter-named bad-request response
  rather than a generic 500 or a silent clamp.
- **SC-008**: A new contributor can run the shipped sample, follow the
  README's "Manual Testing via the Management API" section, and complete
  one full reload-and-observe-drain cycle end-to-end **without writing
  any code**.
- **SC-009**: After this feature ships, the framework's `IShell`
  abstraction exposes each generation's in-flight drain operation as a
  first-class property — the management API's force-drain endpoint and
  per-generation drain snapshots both consume that property, and any
  in-process caller can use it the same way.
