# Phase 0 Research: Deferred Shell Activation and Atomic Shell Reconciliation

## Decision: Model each configured shell with separate desired and applied runtime state

**Rationale**: The current runtime effectively treats `IShellSettingsCache` plus `DefaultShellHost`'s built `ShellContext` cache as one mutable truth. That is incompatible with the feature spec because desired configuration must be recorded immediately even when the runtime cannot apply it yet. The design therefore uses a per-shell runtime record with two distinct layers: the latest configured desired generation and the last committed applied runtime generation, if any. This gives reconciliation a stable place to record drift, deferred outcomes, and last-known-good preservation without mutating away operator intent.

**Alternatives considered**:

- Keep `ShellSettingsCache` as the only authoritative state and encode deferred state indirectly in exceptions or logs: rejected because it cannot represent desired-versus-applied drift explicitly.
- Mutate configured features to match what is currently buildable: rejected because the spec forbids silent feature removal and requires the desired definition to remain authoritative.
- Track only an applied runtime and force operators to infer desired state from providers: rejected because reconciliation status must remain inspectable even when no runtime is currently applied.

## Decision: Refresh the runtime feature catalog as a snapshot before every reconciliation operation

**Rationale**: The revised architecture depends on late-loaded assemblies becoming discoverable during the application lifetime. A one-time feature discovery pass at host initialization is not sufficient. The feature catalog should therefore become a refreshable snapshot produced by re-evaluating all configured `IFeatureAssemblyProvider` sources before startup reconciliation, single-shell reload, and full-shell reload. That snapshot becomes the input to candidate validation and build decisions.

**Alternatives considered**:

- Keep feature discovery one-time and require process restart to pick up new assemblies: rejected because the spec explicitly requires refreshable catalog behavior.
- Refresh feature discovery only for full reloads: rejected because the spec requires one-shell and full-shell reconciliation to use the same refresh-first semantics.
- Merge newly discovered features directly into the existing feature map without a snapshot boundary: rejected because duplicate detection and rollback become ambiguous.

## Decision: Treat duplicate feature IDs and catalog-level inconsistencies as atomic catalog-refresh failures

**Rationale**: The runtime must preserve the previously applied catalog and all previously applied shell runtimes if a refresh discovers duplicate feature IDs or another catalog-level inconsistency. The safest model is a candidate catalog snapshot that is validated in isolation and committed only after the full refresh succeeds. Shell reconciliation must not begin against a partially valid catalog.

**Alternatives considered**:

- Keep the first discovered feature and log a warning for duplicates: rejected because the spec requires explicit failure, not silent precedence.
- Refresh unaffected shells against the new catalog while skipping only the duplicate IDs: rejected because the catalog itself is defined as invalid for that refresh cycle.
- Tear down current applied runtimes before building the replacement catalog: rejected because it violates last-known-good preservation.

## Decision: Reconcile shells through a candidate-build-then-atomic-commit pipeline

**Rationale**: The spec requires the currently serving runtime to remain active until a fully buildable successor for a newer desired generation is ready. That means reconciliation must build and validate a `Candidate Runtime` off to the side, using the latest desired generation and refreshed catalog snapshot, before any applied runtime swap occurs. Only after candidate validation succeeds should the runtime publish deactivation/activation events, replace the applied runtime, and update applied-state metadata as one logical commit.

**Alternatives considered**:

- Evict the old `ShellContext` first and rebuild in place: rejected because failures would create avoidable downtime and lose the last-known-good runtime.
- Record a desired generation as active before the service provider is built: rejected because routing and endpoints must reflect only committed applied runtimes.
- Use a global all-shell cutover barrier that blocks every shell on the slowest candidate: rejected because the spec calls for shell-agnostic reconciliation where unaffected shells continue serving.

## Decision: Preserve the last-known-good applied runtime for both deferred and failed desired generations

**Rationale**: Missing required features are a recoverable deferred outcome, and non-missing-feature candidate build problems are failed outcomes, but neither should tear down a healthy applied runtime for the same shell. The reconciliation record should store the latest desired generation outcome and reason while leaving the prior applied runtime active until a later desired generation can be committed or the shell is explicitly removed from desired state.

**Alternatives considered**:

- Deactivate the shell whenever the newest desired generation cannot be applied: rejected because it creates unnecessary downtime and violates the spec.
- Preserve last-known-good only for missing-feature deferrals, not for general failures: rejected because the spec explicitly requires preservation for non-missing-feature failures too.
- Automatically roll desired state back to the last applied generation: rejected because it would discard operator intent.

## Decision: Make routing, endpoint registration, and active-shell enumeration depend on applied runtime state only

**Rationale**: `WebRoutingShellResolver`, `DefaultShellResolverStrategy`, `ShellEndpointRegistrationHandler`, and `DynamicShellEndpointDataSource` currently reason from configured shells and can therefore over-expose shells that are not safely serving. Under the revised model, those components must consult the applied runtime set only. Desired-only shells stay visible through status inspection but contribute no resolved shell, no endpoints, and no active-shell enumeration until a candidate runtime has been committed.

**Alternatives considered**:

- Let deferred desired generations continue contributing endpoints from configuration alone: rejected because the spec explicitly requires active-only routing and endpoints.
- Keep resolution based on configured shells and fail later during request activation: rejected because it produces misleading routing behavior and late failures.
- Hide deferred or failed shells entirely, including from operator inspection: rejected because the spec requires clear visibility into drift and blocking reasons.

## Decision: Apply the same reconciliation semantics to every configured shell, with explicit `Default` fallback rules

**Rationale**: The spec rejects a `Default`-only activation model. Every configured shell must have the same desired/applied separation and reconciliation lifecycle. `Default` keeps one special rule only in fallback resolution: if a shell named `Default` is explicitly configured but currently unapplied, fallback resolution must report that explicit default as unavailable instead of silently selecting another configured shell. If no explicit `Default` exists, fallback may choose only from shells with an applied active runtime.

**Alternatives considered**:

- Preserve special activation rules only for `Default`: rejected because the feature must apply to every shell.
- When explicit `Default` is unavailable, silently use the first applied shell: rejected because the spec explicitly forbids silent substitution.
- Disable all fallback selection when no explicit `Default` exists: rejected because the existing default-resolution model can still operate safely when limited to applied active runtimes.

## Decision: Add a minimal public runtime-state inspection contract in `CShells.Abstractions`

**Rationale**: Operators and integration code need a stable way to inspect desired generation, applied generation, in-sync status, outcome, and blocking reason for each configured shell. Because that is an external consumer-facing contract, it belongs in `CShells.Abstractions` under the constitution. A small read-only accessor and corresponding status records are sufficient; the candidate build pipeline, catalog refresh internals, and atomic commit mechanics stay implementation-only in `CShells`.

**Alternatives considered**:

- Expose internal caches from `DefaultShellHost` directly: rejected because that leaks implementation details and violates the abstraction-first rule.
- Reuse `IShellManager` for both commands and status reads: rejected because it mixes mutation and inspection concerns and makes the status surface harder to evolve.
- Provide status only through logging and notifications: rejected because operators need queryable current state, not just event history.

## Decision: Tie lifecycle notifications to applied runtime changes, not merely to desired-state updates

**Rationale**: `ShellActivated` and `ShellDeactivating` must describe real applied-runtime transitions. Recording a new desired generation that is deferred or failed is not a successful activation. The notification model should therefore treat desired-state events (`ShellAdded`, `ShellUpdated`, `ShellRemoved`) separately from applied-runtime commit events and ensure aggregate reload notifications convey reconciliation results rich enough for endpoint registration and operator visibility.

**Alternatives considered**:

- Keep publishing `ShellActivated` immediately for every desired update: rejected because it conflicts with active-only semantics.
- Remove desired-state notifications entirely: rejected because explicit configuration changes remain meaningful operator events.
- Require endpoint registration handlers to infer applied-state changes by rebuilding every shell on each event: rejected because it couples web runtime behavior to expensive guesswork instead of clear reconciliation outcomes.

