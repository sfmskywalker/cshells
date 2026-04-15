# Data Model: Deferred Shell Activation and Atomic Shell Reconciliation

## Configured Desired Shell Generation

Represents the latest configured version of a shell that operators want the system to run, regardless of whether the runtime can currently apply it.

### Fields

- `ShellId`: stable shell identifier
- `DesiredGeneration`: monotonically increasing generation or revision token for that shell's desired definition
- `DesiredSettings`: the authoritative `ShellSettings` payload for the generation
- `ConfiguredFeatures`: required configured features after configuration binding and normalization
- `ConfigurationData`: shell-scoped configuration values for that generation
- `RecordedAt`: the time the desired generation became current in the runtime state store

### Validation Rules

- `ShellId` must remain unique among configured shells.
- `DesiredGeneration` must advance whenever the authoritative desired definition changes.
- `ConfiguredFeatures` remain required by default; missing catalog entries do not remove them from desired state.
- Removing a shell from desired state is an explicit operation, not an inferred consequence of deferred or failed reconciliation.

## Applied Shell Runtime

Represents the last committed runtime instance for a shell that is currently safe to serve.

### Fields

- `ShellId`: stable shell identifier
- `AppliedGeneration`: the desired generation from which the current runtime was committed
- `ShellContext`: the committed runtime context and service provider for the shell
- `CatalogGeneration`: the runtime feature catalog snapshot used to build the applied runtime
- `ActivatedAt`: the time the runtime became active
- `IsActive`: whether this applied runtime is currently eligible for routing, endpoints, and active-shell enumeration

### State Transitions

- `NotApplied` → `Active`: the shell's first satisfiable desired generation is built and committed
- `Active(generation N)` → `Active(generation N+1)`: a newer desired generation is built as a candidate and atomically committed
- `Active` → `NotApplied`: the shell is explicitly removed or deactivated and the applied runtime is intentionally torn down

### Validation Rules

- At most one applied runtime may be active per shell at a time.
- `AppliedGeneration` must never be greater than the current `DesiredGeneration`.
- Routing and endpoints may use only `IsActive == true` applied runtimes.

## Runtime Feature Catalog Snapshot

Represents one validated, immutable view of the discoverable feature catalog assembled from all configured feature assembly providers.

### Fields

- `CatalogGeneration`: monotonically increasing snapshot identifier
- `Assemblies`: the resolved assembly set used for discovery during this refresh
- `FeatureDescriptors`: discovered feature descriptors keyed by feature ID
- `ProviderInputs`: the assembly-provider sources that contributed to the snapshot
- `RefreshedAt`: refresh completion time
- `ValidationOutcome`: `Valid` or `Invalid`
- `ValidationFailure`: optional duplicate-ID or catalog-level failure details

### State Transitions

- `Current` → `CandidateRefresh`: provider assemblies are re-evaluated and a new snapshot is built off to the side
- `CandidateRefresh` → `Current`: the candidate snapshot validates successfully and becomes the applied catalog
- `CandidateRefresh` → `Discarded`: duplicate IDs or other catalog-level validation failures prevent commit

### Validation Rules

- A catalog snapshot is committed only if every discovered feature ID is unique.
- An invalid candidate catalog must not replace the current applied catalog.
- Shell candidate builds for one reconciliation pass use the same committed catalog snapshot.

## Candidate Shell Runtime

Represents a not-yet-serving runtime built for a specific desired shell generation against a specific catalog snapshot.

### Fields

- `ShellId`: stable shell identifier
- `TargetDesiredGeneration`: the desired generation this candidate attempts to apply
- `CatalogGeneration`: the catalog snapshot used for validation and build
- `ResolvedFeatures`: ordered feature set used for the candidate build
- `BuildOutcome`: `ReadyToCommit`, `DeferredDueToMissingFeatures`, or `Failed`
- `MissingFeatures`: explicit required feature IDs missing from the committed catalog, if any
- `FailureReason`: non-missing-feature build or validation failure details, if any
- `CandidateContext`: the built `ShellContext` when `BuildOutcome == ReadyToCommit`

### State Transitions

- `Planned` → `DeferredDueToMissingFeatures`: required configured features are absent from the committed catalog
- `Planned` → `Failed`: feature dependency resolution, configuration binding, service registration, or validation fails for a non-missing-feature reason
- `Planned` → `ReadyToCommit`: the runtime is fully built and validated off to the side
- `ReadyToCommit` → `Committed`: the candidate atomically replaces the previous applied runtime, if any
- `ReadyToCommit` → `Discarded`: a newer desired generation supersedes the candidate before commit

### Validation Rules

- `MissingFeatures` is populated only for deferred outcomes.
- `FailureReason` is populated only for failed outcomes.
- `CandidateContext` exists only for `ReadyToCommit` candidates.
- A candidate must never become routable before the commit transition completes.

## Shell Reconciliation Status

Represents the operator-visible record for one configured shell after a reconciliation pass.

### Fields

- `ShellId`: stable shell identifier
- `DesiredGeneration`: latest configured desired generation
- `AppliedGeneration`: nullable currently committed generation
- `Outcome`: `Active`, `DeferredDueToMissingFeatures`, or `Failed`
- `IsInSync`: whether `AppliedGeneration` matches `DesiredGeneration`
- `IsRoutable`: whether the shell currently has an applied active runtime
- `DriftReason`: explanation when `IsInSync == false`
- `BlockingReason`: explanation when the latest desired generation is deferred or failed
- `MissingFeatures`: required feature IDs still blocking application, if any

### Relationships

- Every configured shell has exactly one current reconciliation status.
- A status references one current desired generation.
- A status references zero or one applied runtime.
- A status may reference the latest candidate outcome even when the applied runtime remains older and active.

### Validation Rules

- `Outcome == Active` does not imply `IsInSync == true`; a shell may remain active on the last-known-good applied generation while the newest desired generation is deferred or failed.
- `IsRoutable` is true only when an applied runtime exists and is active.
- `BlockingReason` is required whenever the latest desired generation is not currently applied.

## Default Shell Resolution State

Represents fallback-resolution eligibility for the shell named `Default` and the runtime's applied-shell fallback policy.

### Fields

- `HasExplicitDefault`: whether a shell named `Default` exists in configured desired state
- `DefaultDesiredGeneration`: the latest desired generation for the explicit default shell, if configured
- `DefaultAppliedGeneration`: the currently applied generation for the explicit default shell, if any
- `IsExplicitDefaultAvailable`: whether the explicit default currently has an applied active runtime
- `FallbackCandidateShellIds`: shells eligible for fallback only when no explicit default shell is configured

### Validation Rules

- If `HasExplicitDefault == true` and `IsExplicitDefaultAvailable == false`, fallback resolution must report `Default` as unavailable rather than selecting another shell.
- `FallbackCandidateShellIds` must contain only shells with applied active runtimes.
- If no shells have an applied active runtime, fallback resolution yields no shell.

## Reconciliation Operation

Represents one startup, single-shell, or full-shell runtime reconciliation pass.

### Fields

- `Scope`: `Startup`, `SingleShell`, or `AllShells`
- `TargetShellId`: nullable; populated only for single-shell reconciliation
- `DesiredStateSet`: configured shells considered during the pass
- `CatalogGeneration`: the committed feature catalog snapshot used by the pass
- `PerShellStatuses`: resulting status records for affected shells
- `CommittedShellIds`: shells whose applied runtimes changed during the pass
- `FailedShellIds`: shells whose latest desired generation ended in `Failed`
- `DeferredShellIds`: shells whose latest desired generation ended in `DeferredDueToMissingFeatures`

### Validation Rules

- Every reconciliation operation refreshes the catalog before evaluating shell candidate builds.
- Single-shell and all-shell reconciliation share the same candidate-build and atomic-commit semantics.
- A catalog-refresh failure aborts the operation before any `CommittedShellIds` are recorded.

