# Feature Specification: Single-Provider Blueprint Simplification

**Feature Branch**: `008-single-provider-simplification`
**Created**: 2026-04-25
**Status**: Draft
**Input**: User description: "Drop multi-provider composition introduced in 007: replace the composite + cursor codec with a single-provider model where AddShell defaults to the in-memory provider and external providers must be registered exactly once."

## Overview

Feature `007` introduced a multi-provider composition model: the registry consults a
`CompositeShellBlueprintProvider` that fans out to N registered
`IShellBlueprintProvider` instances, with deterministic precedence, opaque-cursor
pagination across providers, and lazy duplicate detection. After shipping `007` and
weighing real-world deployment patterns, the multi-provider machinery is being
retired because the realistic mixed-source scenarios it enables are uncommon, the
workarounds are simple, and the maintenance surface is significant.

This feature replaces the multi-provider model with a strict **single-provider**
model:

- The registry depends on **exactly one** `IShellBlueprintProvider` resolved from DI.
- The default registration is the built-in `InMemoryShellBlueprintProvider`, which
  backs the fluent `AddShell(...)` API. Hosts that only use code-defined shells get
  this for free.
- Hosts that register an external provider do so via
  `CShellsBuilder.AddBlueprintProvider(factory)`. The external provider replaces
  the in-memory default. `AddShell(...)` calls alongside an external provider raise
  a clear composition-time error directing the host to put the shells in the
  external source.

**API ergonomics — `AddShell` is shorthand for the in-memory provider, not a
privileged code-first abstraction.** Conceptually every host chooses exactly one
blueprint source. The fluent `c.AddShell(...)` API is convenient sugar for the
common case where that source is the built-in in-memory provider — it is *not* a
top-level "code-first mode" that is somehow distinct from the provider model.
This asymmetry is deliberate and follows the ASP.NET Core convention (`AddControllers`
at the top level, explicit `WithXxx` for deviations): the most common case stays
ergonomic, and the "exactly one provider" constraint is taught by the fail-fast
error rather than imposed on every newcomer through nested provider builders.

**The provider contract is open and extensible.** `IShellBlueprintProvider` is the
sole abstraction the registry depends on, and any implementation — first-party or
third-party, in-memory or backed by a database, blob store, distributed cache,
remote API, or anything else — plugs in through the same `AddBlueprintProvider`
seam. The framework ships two reference providers
(`ConfigurationShellBlueprintProvider`, `FluentStorageShellBlueprintProvider`) as
concrete examples; they are not the supported universe. Hosts and downstream
NuGet packages are expected to add their own providers as their needs dictate.

**This is a clean overhaul.** The composite, the cursor codec, the
composite-options toggle, and the duplicate-blueprint exception are all deleted
outright. Tests, sample code, and downstream extension methods are migrated in the
same PR. Hosts whose configuration matched the supported single-source patterns
observe **no API change**; hosts mixing `AddShell` with an external provider must
move their code-defined shells into the external source (or implement their own
composing provider).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Code-only host registers shells via `AddShell` and activates them lazily (Priority: P1)

A host developer registers shells purely through the fluent API:

```csharp
builder.Services.AddCShells(c => c
    .AddShell("payments", s => s.WithFeature<PaymentsFeature>())
    .AddShell("billing",  s => s.WithFeature<BillingFeature>()));
```

No external provider is configured. The built-in in-memory provider is
auto-registered. First request for either name lazily activates the shell — exactly
the behavior shipped in `007` for this case, with no behavioral change.

**Why this priority**: This is the most common usage pattern and the simplest. It
must continue to work without modification.

**Independent Test**: Build a host with two `AddShell` calls, verify
`registry.GetOrActivateAsync("payments")` returns an active generation 1 shell on
first call.

**Acceptance Scenarios**:

1. **Given** a host with `AddShell("a")` and `AddShell("b")` and no
   `AddBlueprintProvider`, **When** the host starts, **Then** the in-memory
   provider is auto-registered and contains both blueprints.
2. **Given** the same host, **When** `GetOrActivateAsync("a")` is called, **Then**
   the in-memory provider is consulted and returns the blueprint for `a`.

---

### User Story 2 — External-provider host registers a single source (Priority: P1)

A host developer registers any external `IShellBlueprintProvider` as the sole
source of blueprints. The shipped first-party extensions are convenient one-liners
on top of the underlying primitive:

```csharp
builder.Services.AddCShells(c => c.WithConfigurationProvider(builder.Configuration));
// — or —
builder.Services.AddCShells(c => c.WithFluentStorageBlueprints(blobStorage));
// — or any custom provider, registered via the underlying primitive: —
builder.Services.AddCShells(c => c.AddBlueprintProvider(sp => new MySqlShellBlueprintProvider(connStr)));
```

All three shapes register exactly one `IShellBlueprintProvider`; they differ only
in whether the framework supplies a sugar method for the common case. There is no
composite. Lookups go directly to whichever provider is registered; pagination
uses its native cursor; mutations route to its manager (when present).

**Why this priority**: This is the second canonical usage pattern. After Story 1,
it is the most common way to deploy CShells. Equally important: *third-party
providers must work through the same seam* — any database, cache, remote-API, or
custom-store implementation of `IShellBlueprintProvider` is registered via
`AddBlueprintProvider` without any framework change.

**Independent Test**: Build a host that registers an arbitrary
`IShellBlueprintProvider` via `AddBlueprintProvider` (using a stub provider for
the test), verify `registry.GetOrActivateAsync(name)` for any name the stub
claims activates a generation 1 shell. The first-party shipped sugars
(`WithConfigurationProvider`, `WithFluentStorageBlueprints`) are tested
separately as concrete instances of the same pattern.

**Acceptance Scenarios**:

1. **Given** a host with `AddBlueprintProvider(sp => new SomeCustomProvider())`
   and no `AddShell` calls, **When** the host starts, **Then** the custom provider
   is the single registered `IShellBlueprintProvider`.
2. **Given** a host with `WithConfigurationProvider(config)` and no `AddShell`
   calls, **When** the host starts, **Then** the configuration provider is the
   single registered `IShellBlueprintProvider`.
3. **Given** the same host, **When** `GetOrActivateAsync("acme")` is called and
   `appsettings.json` defines an `acme` section under `Shells`, **Then** the shell
   activates as generation 1.
4. **Given** a host with `WithFluentStorageBlueprints(blobStorage)`, **When**
   `GetOrActivateAsync("tenant-x")` is called and a `tenant-x.json` blob exists,
   **Then** the shell activates as generation 1.

---

### User Story 3 — Mixing `AddShell` with an external provider fails fast at composition time (Priority: P1)

A host developer accidentally combines code-defined shells with an external
provider:

```csharp
builder.Services.AddCShells(c => c
    .AddShell("platform", s => s.WithFeature<PlatformFeature>())
    .WithConfigurationProvider(builder.Configuration));
```

This combination is no longer supported. The host throws at composition time (when
the service provider is built or when the registry is first resolved) with a clear
error explaining the constraint and offering migration guidance: "register all
shells in your external source, or implement a single combining provider."

**Why this priority**: Without a fail-fast guard, hosts that mix sources would
silently lose their `AddShell` blueprints (since the external provider would
replace the in-memory default). A clear composition-time error is critical to
prevent silent breakage during the migration from `007` to `008`.

**Independent Test**: Build a host that calls both `AddShell(...)` and
`AddBlueprintProvider(...)`, verify service-provider construction (or first
registry resolution) throws an `InvalidOperationException` whose message names
both sources and offers migration guidance.

**Acceptance Scenarios**:

1. **Given** a host that calls `AddShell("a")` followed by
   `WithConfigurationProvider(...)`, **When** the service provider is built (or the
   registry is first resolved), **Then** an `InvalidOperationException` is raised
   whose message identifies the conflict.
2. **Given** the same conflict in the reverse order
   (`WithConfigurationProvider` first, then `AddShell`), **When** the same trigger
   fires, **Then** the same exception is raised.
3. **Given** a host that calls `AddBlueprintProvider(...)` twice (two external
   providers), **When** the trigger fires, **Then** an `InvalidOperationException`
   is raised stating that exactly one provider is permitted.

---

### User Story 4 — Existing `007` consumers migrate cleanly (Priority: P2)

A consumer of `007` that uses a single source pattern (just `AddShell`, just
`WithConfigurationProvider`, or just `WithFluentStorageBlueprints`) observes no API
change and no behavioral change. Their code compiles and runs identically.

A consumer that mixed `AddShell` with an external provider receives the
fail-fast error from Story 3 and migrates by moving their code-defined shells into
the external source (or by writing a custom combining provider, which they own).

**Why this priority**: P2 because the migration burden falls on the small subset
of hosts using mixed-source composition. The fail-fast guard from Story 3 makes
the migration immediate and safe.

**Independent Test**: Run the existing `007` test suite against `008` after the
simplification; tests covering single-source patterns continue to pass; tests
covering composite/multi-source patterns are removed (their scenarios no longer
exist).

**Acceptance Scenarios**:

1. **Given** a host using only `AddShell(...)`, **When** the simplification ships,
   **Then** the host requires zero code changes and behaves identically.
2. **Given** a host using only `WithConfigurationProvider(...)`, **When** the
   simplification ships, **Then** the host requires zero code changes.
3. **Given** the existing 007 test suite, **When** it runs against the simplified
   surface, **Then** every single-source test continues to pass and the
   composite-only tests are removed (not migrated).

---

### Edge Cases

- **No provider configured at all**: a host that calls `AddCShells` with neither
  `AddShell` nor `AddBlueprintProvider` ends up with the in-memory provider
  registered but empty. Every `GetOrActivateAsync` raises
  `ShellBlueprintNotFoundException`. This is legal and documented.
- **`AddShell` after `AddBlueprintProvider`**: caught by the composition-time
  guard (Story 3, scenario 2). The order of registration calls does not matter.
- **Multiple `AddBlueprintProvider` calls**: caught by the composition-time guard
  (Story 3, scenario 3). Only one external provider is permitted.
- **`AddBlueprintProvider` factory returns null**: existing guard-clause behavior
  applies — `ArgumentNullException` raised when the registry resolves the factory.
- **Custom composing provider**: a host that genuinely needs multi-source
  composition (e.g., legitimate mixed-source) implements its own
  `IShellBlueprintProvider` that internally fans out to its sub-sources. The
  framework does not provide one; this is by design.

## Requirements *(mandatory)*

### Functional Requirements

**Single-provider model**

- **FR-001**: The registry MUST depend on exactly one `IShellBlueprintProvider`
  instance resolved from DI.
- **FR-002**: When neither `AddShell(...)` nor `AddBlueprintProvider(...)` is
  invoked on `CShellsBuilder`, the framework MUST register the built-in
  `InMemoryShellBlueprintProvider` (empty) as the single
  `IShellBlueprintProvider`.
- **FR-003**: When `AddShell(...)` is invoked one or more times and
  `AddBlueprintProvider(...)` is NOT invoked, the framework MUST register the
  built-in `InMemoryShellBlueprintProvider` (populated with the added blueprints)
  as the single `IShellBlueprintProvider`.
- **FR-004**: When `AddBlueprintProvider(factory)` is invoked exactly once and
  `AddShell(...)` is NOT invoked, the framework MUST register the
  factory-resolved provider as the single `IShellBlueprintProvider`.
- **FR-005**: When `AddShell(...)` and `AddBlueprintProvider(...)` are BOTH
  invoked, host startup MUST fail with an `InvalidOperationException`. The
  exception message MUST:
  (a) state that `AddShell` registers blueprints with the in-memory provider and
      `AddBlueprintProvider`-registered providers are external, and that exactly
      one provider is permitted;
  (b) enumerate the three valid resolutions: move the `AddShell` blueprints into
      the external source, drop the external provider, OR implement a custom
      `IShellBlueprintProvider` that combines both sources.
  Rationale: the asymmetric API (`AddShell` at the top level vs. `WithXxx` for
  external providers) is optimized for the common case; the error message is the
  primary teaching surface for the "exactly one provider" constraint, so it
  must do the teaching well.
- **FR-006**: When `AddBlueprintProvider(...)` is invoked more than once, host
  startup MUST fail with an `InvalidOperationException` stating that exactly one
  external provider is permitted.
- **FR-006a**: The framework MUST treat `IShellBlueprintProvider` as an
  open-ended extension point. Any first- or third-party implementation —
  including but not limited to in-memory, configuration-backed, blob-backed,
  database-backed, distributed-cache-backed, remote-API-backed, or hand-written
  combining providers — MUST be registerable via `AddBlueprintProvider(factory)`
  without any framework modification. The shipped reference providers
  (`InMemoryShellBlueprintProvider`, `ConfigurationShellBlueprintProvider`,
  `FluentStorageShellBlueprintProvider`) are concrete examples of this contract,
  not the supported universe.

**Removals**

- **FR-007**: The framework MUST remove `CompositeShellBlueprintProvider`,
  `CompositeCursorCodec`, `CompositeProviderOptions`, and the
  `CompositeCursorEntry` record type. No replacement is provided.
- **FR-008**: The framework MUST remove `DuplicateBlueprintException` from
  `CShells.Abstractions`. The error condition it represented (two providers claim
  the same name) is no longer reachable.
- **FR-009**: The registry MUST remove the
  `ShouldWrapAsUnavailable`/`LookupBlueprintAsync` exclusion of
  `DuplicateBlueprintException` since the type no longer exists.
- **FR-010**: All test files exclusively covering composite or
  duplicate-detection behavior MUST be removed:
  `CompositeShellBlueprintProviderTests.cs`,
  `CompositeCursorCodecTests.cs`, and any test methods asserting
  `DuplicateBlueprintException` semantics.
- **FR-011**: The "track owning provider type" `Dictionary<string, Type>` added
  to `CompositeShellBlueprintProvider.ListAsync` during the post-`#88` review
  fix is removed alongside the composite class.

**Builder and DI updates**

- **FR-012**: `CShellsBuilder.AddBlueprintProvider(factory)` MUST be retained as
  the public registration API for an external provider. Its semantics change:
  exactly one call permitted; alongside `AddShell` is forbidden.
- **FR-013**: `CShellsBuilder.AddShell(name, configure)` and
  `CShellsBuilder.AddBlueprint(blueprint)` MUST continue to populate the
  in-memory provider's blueprint list. Their public signatures are unchanged.
- **FR-014**: `CShellsBuilder.PreWarmShells(params string[])` MUST continue to
  work unchanged (independent of provider source).

**Migration of shipped reference extensions**

The framework ships two reference extension methods that wrap
`AddBlueprintProvider` for the two providers it currently bundles. These are
sugars over the open contract — third-party providers may ship equivalent
extension methods of their own.

- **FR-015**: `WithConfigurationProvider(...)` MUST register
  `ConfigurationShellBlueprintProvider` via `AddBlueprintProvider`. Its
  signature is unchanged.
- **FR-016**: `WithFluentStorageBlueprints(...)` MUST register
  `FluentStorageShellBlueprintProvider` via `AddBlueprintProvider`. Its signature
  is unchanged.

**Documentation**

- **FR-017**: The XML doc-comments and any sample code referencing
  `CompositeShellBlueprintProvider`, the cursor codec, or
  `DuplicateBlueprintException` MUST be removed or rephrased to describe the
  single-provider model.

### Key Entities

- **`IShellBlueprintProvider`** — the open extension point. Unchanged contract:
  lazy `GetAsync(name)`, optional `ExistsAsync`, paginated `ListAsync(query)`.
  Now resolved from DI as a single instance instead of an `IEnumerable`. Any
  implementation may be registered.

The following are **shipped reference implementations** of the contract — useful
defaults for common cases, not the bounds of what the framework supports:

- **`InMemoryShellBlueprintProvider`** — unchanged. Backs `AddShell(...)`
  registrations.
- **`ConfigurationShellBlueprintProvider`** — unchanged. Backs
  `WithConfigurationProvider(...)`.
- **`FluentStorageShellBlueprintProvider`** — unchanged. Backs
  `WithFluentStorageBlueprints(...)` and implements `IShellBlueprintManager` for
  mutation.

Deleted by this feature:

- **`CompositeShellBlueprintProvider`** — DELETED.
- **`CompositeCursorCodec`**, **`CompositeCursorEntry`**, **`CompositeProviderOptions`** — DELETED.
- **`DuplicateBlueprintException`** — DELETED.

### Assumptions

- Hosts that need multi-source composition are rare enough that requiring them to
  implement their own combining `IShellBlueprintProvider` is acceptable. The
  framework's stance is "one source at a time"; combining is a host concern.
- The composition-time guard (FR-005, FR-006) is enforced when `IShellRegistry`
  is first resolved from DI, which happens at host startup via the
  `CShellsStartupHostedService`. Hosts therefore always discover the
  misconfiguration before serving any traffic.
- No external NuGet package consumers of `007`'s `DuplicateBlueprintException`
  exist (CShells is a preview prerelease; the type was added in `007` and never
  observed in production).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Total deletion: at least 350 lines of production code removed
  (`CompositeShellBlueprintProvider` + `CompositeCursorCodec` +
  `CompositeProviderOptions` + `DuplicateBlueprintException` + the
  duplicate-tracking dictionary added in the post-#88 fix).
- **SC-002**: At least 8 test methods (across composite and cursor-codec test
  files) removed; remaining test count stays at or above `385` after the
  simplification (single-source coverage preserved).
- **SC-003**: A host with only `AddShell(...)` calls compiles and behaves
  identically to its `007` form — zero code changes required.
- **SC-004**: A host with only `WithConfigurationProvider(...)` or only
  `WithFluentStorageBlueprints(...)` compiles and behaves identically — zero code
  changes required.
- **SC-005**: A host that mixes `AddShell(...)` with an external provider raises
  `InvalidOperationException` at startup whose message names both sources and
  references the migration guidance.
- **SC-006**: After the simplification, the public DI surface for blueprint
  sourcing is exactly: `AddShell`, `AddBlueprint`, `AddBlueprintProvider`,
  `WithConfigurationProvider`, `WithFluentStorageBlueprints`. No
  composite-related public types remain.
- **SC-007**: The 007-era `DuplicateBlueprintException` type is no longer
  present in `CShells.Abstractions` after the simplification.
- **SC-008**: A test-only third-party `IShellBlueprintProvider` (i.e., one not
  shipped by the framework) registered via `AddBlueprintProvider` activates and
  serves shells identically to the shipped reference providers — demonstrating
  that the extension seam is open and the provider contract is fully sufficient
  for external implementations.
