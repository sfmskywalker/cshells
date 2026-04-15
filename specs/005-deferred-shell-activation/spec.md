# Feature Specification: Deferred Shell Activation and Atomic Shell Reconciliation

**Feature Branch**: `005-deferred-shell-activation`  
**Created**: 2026-04-15  
**Status**: Draft  
**Input**: User description: "Revise the existing deferred shell activation feature to prefer a cleaner ideal architecture with breaking changes allowed. The model must apply to every shell, not only `Default`, and must explicitly separate configured desired state from applied runtime state. Reconciliation must be atomic: the last-known-good active runtime stays in service until a valid successor runtime for a newer desired generation is fully buildable and ready to commit. Configuration updates that reference not-yet-available features must become pending or deferred desired state instead of tearing down the currently active runtime. Once late-loaded assemblies become available and the refreshed feature catalog satisfies the desired shell, the system must build a candidate runtime and atomically swap to it. Routing and endpoints must stay active-only. The spec must preserve refreshable feature catalog behavior, duplicate feature ID failure, deferred or failed outcomes, partial startup, and explicit semantics for an explicitly configured `Default` shell with no silent substitution." 

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preserve Live Service While New Desired State Waits (Priority: P1)

As an application operator, I want each shell's latest configured definition to be recorded immediately while the last-known-good applied runtime stays live until a newer valid runtime is ready, so configuration changes never silently discard intent or cause avoidable downtime.

**Why this priority**: This is the central architecture shift. The feature must separate what operators want the shell to be from what runtime is currently safe to serve.

**Independent Test**: Start with an active shell, update its configuration so the new desired version references a not-yet-available feature, and confirm that the new desired version is recorded as pending with a deferred reason while the previously applied runtime remains active and routable.

**Acceptance Scenarios**:

1. **Given** a shell already has an active applied runtime and a newer configuration version references a required feature that is missing from the refreshed feature catalog, **When** reconciliation evaluates that newer desired version, **Then** the newer desired version is recorded as `DeferredDueToMissingFeatures`, the previously applied runtime remains in service, and the shell is marked as out of sync until reconciliation succeeds.
2. **Given** a shell has no previously applied runtime and its configured desired version references one or more missing required features, **When** startup reconciliation runs, **Then** the shell has no active runtime, its desired version is recorded as `DeferredDueToMissingFeatures`, and its configured features remain intact rather than being silently removed.
3. **Given** a shell already has an active applied runtime and a newer desired version fails to build for a reason other than missing required features, **When** reconciliation attempts to build a replacement runtime, **Then** the newer desired version is recorded as `Failed`, the last-known-good applied runtime remains in service, and the failure reason is visible to operators.
4. **Given** a shell named `Default` is explicitly configured and its latest desired version is not currently applied, **When** fallback shell resolution is attempted, **Then** the system does not silently substitute a different configured shell for that explicit default shell.

---

### User Story 2 - Atomically Reconcile All Shells After Catalog Refresh (Priority: P2)

As an application operator, I want runtime refresh and reload operations to refresh the feature catalog and reconcile every affected shell atomically, so newly satisfiable desired versions can replace older runtimes without partial cutovers or service gaps.

**Why this priority**: Deferred activation only becomes operationally useful when late-arriving feature assemblies can move shells from pending desired state to applied runtime state without restarts or unstable transitions.

**Independent Test**: Start with one shell deferred and another shell running an older applied runtime because its newer desired version is currently unsatisfied, then make the missing features discoverable, trigger reconciliation, and confirm that the refreshed catalog enables candidate build and atomic replacement for each newly satisfiable shell.

**Acceptance Scenarios**:

1. **Given** any shell has a desired version that is newer than its applied runtime because required features were previously missing, **When** a refresh or reload operation runs after those features become discoverable, **Then** the system refreshes the feature catalog first, builds a candidate runtime for the desired version, and atomically swaps to that new runtime only after it is fully ready.
2. **Given** some shells are already in sync and others are pending newer desired versions, **When** a full reconciliation runs after a catalog refresh, **Then** each shell is re-evaluated independently against the refreshed catalog and latest desired configuration, allowing newly satisfiable shells to advance without disrupting shells that already have valid applied runtimes.
3. **Given** a catalog refresh discovers duplicate feature IDs from the configured assembly-provider set, **When** reconciliation is requested, **Then** the refresh fails explicitly and the previously applied catalog and all previously applied shell runtimes remain unchanged.

---

### User Story 3 - Expose Clear Desired-vs-Applied Status to Operators and Routing (Priority: P3)

As a CShells maintainer, I want routing, endpoints, and operator-visible status to reflect the applied runtime state while also exposing drift from the latest desired state, so the system serves only committed active runtimes and still explains why a shell is pending or blocked.

**Why this priority**: The architecture is only trustworthy if operators can distinguish "what is configured now" from "what is actually serving now" and if request handling respects that distinction.

**Independent Test**: Exercise startup and later reconciliation with shells in mixed conditions, including in-sync active shells, shells with active last-known-good runtimes but pending desired updates, and shells with no applied runtime, then confirm that only committed active runtimes are routable and that status inspection shows desired generation, applied generation, drift, and blocking reason for every shell.

**Acceptance Scenarios**:

1. **Given** a shell has an older applied runtime still serving while a newer desired version is deferred, **When** endpoint registration and request resolution are evaluated, **Then** routing and endpoints continue to use only the applied active runtime while operator status shows the newer desired version as pending with its blocking reason.
2. **Given** one or more shells have no applied runtime because their desired versions are deferred or failed, **When** endpoint registration and request resolution are evaluated, **Then** those shells contribute no routable endpoints and no successful resolution outcomes.
3. **Given** startup or reload finishes with a mix of in-sync shells, shells serving last-known-good runtimes, and shells with no applied runtime, **When** operators inspect shell status, **Then** they can see the desired generation, applied generation if any, reconciliation outcome, and blocking or drift reason for each configured shell.

### Edge Cases

- A catalog refresh discovers the same feature ID from two different late-loaded assemblies; the refresh must fail explicitly before any applied catalog or applied shell runtime is changed.
- A shell is active on generation N, but generation N+1 is configured with a missing required feature; the system must keep generation N serving while recording generation N+1 as deferred pending desired state.
- A shell has no previously applied runtime and its first desired generation cannot be satisfied; startup must leave that shell unapplied while still allowing unrelated shells to activate.
- A refreshed catalog again makes a pending desired generation satisfiable after several unsuccessful attempts; the system must build a fresh candidate and atomically commit it without requiring manual re-entry of the desired configuration.
- A shell's desired generation becomes invalid for a non-missing-features reason while an older applied runtime remains healthy; the system must preserve the older applied runtime and record the newer desired generation as failed.
- A configured `Default` shell is pending or failed while other shells are active; fallback resolution must not silently route requests to a different shell that the operator did not explicitly configure as the default.
- A catalog refresh removes a feature required by the latest desired generation of multiple shells while older applied runtimes still exist; each shell must record deferred drift independently while preserving its last-known-good applied runtime until a valid successor can be committed or the desired state changes.
- No shells have an applied runtime after reconciliation because every configured shell is deferred or failed; routing must expose no shell-backed endpoints and operator status must report the system as configured but currently unapplied.

## Two-Layer Shell Truth Model

### Per-Shell Sources of Truth

| Layer | Meaning | Update rule |
| --- | --- | --- |
| Configured desired state | The latest configured shell definition, including required features and configuration, regardless of whether it can currently run. | It updates whenever configuration providers publish a newer shell definition. |
| Applied runtime state | The last committed runtime for that shell that is safe to serve, if one exists. | It changes only after a candidate runtime for a target desired generation has been fully built, validated, and atomically committed. |

### Reconciliation Outcome Vocabulary

| Outcome | Meaning | Availability rules |
| --- | --- | --- |
| `Active` | The applied runtime exists and is currently serving. This may be fully in sync with desired state or may be the last-known-good runtime while a newer desired generation is pending. | Only the applied active runtime may be resolved, expose endpoints, and participate in active lifecycle behavior. |
| `DeferredDueToMissingFeatures` | The latest desired generation cannot yet be built because one or more required features are not present in the refreshed feature catalog. | If an older applied runtime exists, it remains active until a valid successor is ready; otherwise the shell has no applied runtime and is unavailable. |
| `Failed` | The latest desired generation cannot currently be built for a reason other than missing required features. | If an older applied runtime exists, it remains active until a later desired generation can be committed; otherwise the shell has no applied runtime and is unavailable. |

### Required Operator Visibility Per Shell

Operators and runtime components must be able to inspect, for each configured shell, the latest desired generation, the currently applied generation if any, whether those generations are in sync, the reconciliation outcome for the latest desired generation, and the blocking or drift reason when the desired generation is not currently applied.

## Current Architecture Interaction Analysis

| Component | Current role in CShells | Required behavior direction for this feature |
| --- | --- | --- |
| `DefaultShellHost` | Discovers features, validates configured features, and lazily builds `ShellContext` instances from `ShellSettings`. | It must separate desired shell definitions from applied shell runtimes, preserve last-known-good applied runtimes, and participate in candidate-build-then-commit reconciliation instead of treating one mutable runtime view as the only truth. |
| `DefaultShellManager` | Reloads one shell or all shells from provider data and evicts or rebuilds runtime contexts. | It must refresh the feature catalog before evaluation, compute the latest desired generation for each affected shell, build candidate runtimes off to the side, and atomically commit only those candidates that are fully ready while leaving existing applied runtimes untouched on deferred or failed outcomes. |
| `ShellStartupHostedService` | Eagerly activates all shells at startup and publishes startup lifecycle notifications. | It must support partial startup by recording desired state for every configured shell, applying runtimes only for satisfiable shells, and leaving the rest deferred or failed without deleting their desired definitions. |
| `ShellEndpointRegistrationHandler` | Registers or removes shell endpoints in response to shell lifecycle notifications. | It must treat committed applied active runtimes as the only endpoint-eligible source and react to atomic swaps, removals, and loss of applied runtime without exposing pending desired generations. |
| Feature assembly providers | Supply assemblies used for feature discovery, including host and explicit assembly sources. | They must participate in repeatable catalog refresh cycles so late-loaded assemblies can satisfy pending desired generations after startup. |
| Resolver behavior, including `DefaultShellResolverStrategy` | Chooses which shell should handle a request, with a fallback that currently resolves `Default`. | Resolution must consult only applied active runtimes. If `Default` is explicitly configured but does not currently have an applied runtime for its latest desired state, the system must report that explicit default as unavailable rather than silently substituting another shell. |

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST treat configured features as required by default and MUST NOT silently drop a configured feature merely because that feature is missing from the current runtime feature catalog.
- **FR-002**: The system MUST maintain, for every configured shell, a configured desired state that is authoritative even when no runtime can currently be applied.
- **FR-003**: The system MUST maintain, separately from desired state, an applied runtime state representing the last committed runtime that is currently safe to serve, if any.
- **FR-004**: The system MUST expose whether each shell's desired state and applied runtime state are in sync or whether the shell is serving a last-known-good applied runtime while a newer desired generation remains pending.
- **FR-005**: When a newer desired shell generation references one or more required features that are missing from the refreshed runtime feature catalog, the system MUST record that desired generation as `DeferredDueToMissingFeatures` instead of mutating its configured feature set.
- **FR-006**: When a shell has an existing applied runtime and its newer desired generation is `DeferredDueToMissingFeatures`, the system MUST preserve the existing applied runtime in service until a valid successor runtime for a newer desired generation is fully ready to commit or the shell is explicitly removed from desired state.
- **FR-007**: When a shell has no existing applied runtime and its desired generation is `DeferredDueToMissingFeatures`, the system MUST keep the shell unapplied and unavailable for routing while preserving the full desired configuration.
- **FR-008**: If a desired shell generation cannot be built for a reason other than missing required features, the system MUST record that desired generation as `Failed` and MUST preserve any existing applied runtime for that shell until a valid successor is ready or the shell is explicitly removed from desired state.
- **FR-009**: The system MUST retain the reason for each deferred or failed desired generation, including which required features are missing for deferred outcomes and what failure blocked candidate build or validation for failed outcomes.
- **FR-010**: The system MUST provide a refreshable runtime feature catalog that can re-evaluate all configured feature assembly providers multiple times during the application lifetime.
- **FR-011**: Every shell reload or shell-set reconciliation operation MUST refresh the runtime feature catalog before evaluating and mutating shell runtime state.
- **FR-012**: If a catalog refresh detects duplicate feature IDs or any other catalog-level inconsistency, the refresh MUST fail explicitly and MUST leave the previously applied catalog and all previously applied shell runtimes unchanged.
- **FR-013**: For any shell whose desired generation is eligible to advance, the system MUST build and validate a candidate runtime before changing the currently applied runtime for that shell.
- **FR-014**: Reconciliation MUST commit a shell runtime update atomically: the system MUST keep the current applied runtime serving until the successor runtime for the target desired generation is fully ready, and the system MUST swap from old applied runtime to new applied runtime as one logical commit.
- **FR-015**: Once a pending or deferred desired generation becomes satisfiable after a later catalog refresh, the system MUST build a candidate runtime for that desired generation and atomically promote it to the applied runtime during the same reconciliation pass.
- **FR-016**: Startup MUST support partial startup by recording desired state for every configured shell, applying runtimes only for shells whose desired generations are buildable, and leaving other shells deferred or failed without blocking unrelated shells.
- **FR-017**: Shell reconciliation semantics MUST apply to every configured shell, not only to `Default`, although the spec MUST retain explicit resolver behavior for an explicitly configured `Default` shell.
- **FR-018**: If a shell named `Default` is explicitly configured but does not currently have an applied runtime for its latest desired state, default-shell resolution MUST report that shell as unavailable and MUST NOT silently substitute a different configured shell.
- **FR-019**: If no shell named `Default` is configured, any fallback default-shell selection behavior MUST choose only from shells that currently have an applied active runtime.
- **FR-020**: Only shells with an applied active runtime MUST be eligible for request resolution, route registration, endpoint exposure, and active-shell enumeration used by the web runtime.
- **FR-021**: A pending desired generation that is deferred or failed MUST NOT expose endpoints or routing independently of the currently applied runtime.
- **FR-022**: Endpoint-registration behavior MUST respond to atomic shell commits so newly applied runtimes gain endpoints only after commit, and shells that lose their applied runtime through removal or explicit deactivation lose endpoints before reconciliation is considered complete.
- **FR-023**: Lifecycle behavior tied to shell activation and deactivation MUST apply only to changes in applied active runtime state; recording a new desired generation that has not yet been committed MUST NOT be treated as a completed activation.
- **FR-024**: The system MUST expose, for every configured shell, the desired generation, applied generation if any, reconciliation outcome, in-sync or out-of-sync status, and blocking or drift reason so operators can inspect why a shell is active, pending, deferred, or failed.
- **FR-025**: A configured shell MUST remain part of desired state until it is removed from shell configuration; deferred or failed outcomes for the latest desired generation MUST NOT imply silent deletion.
- **FR-026**: Runtime management behavior for one-shell reloads and full-shell reconciliation MUST use the same catalog-refresh-first, candidate-build, and atomic-commit semantics.
- **FR-027**: Any future support for optional configured features MUST require explicit modeling in shell configuration or feature metadata; optionality MUST NOT be inferred from missing catalog entries.
- **FR-028**: The design for this feature MAY introduce breaking changes to existing public contracts and internal architecture where needed to support separate desired and applied state, atomic runtime replacement, and refreshable catalog behavior.

### Key Entities *(include if feature involves data)*

- **Configured Desired Shell Generation**: The latest configured version of a shell, including its required features and configuration, regardless of whether it can currently be applied.
- **Applied Shell Runtime**: The last committed runtime instance for a shell that is currently serving traffic, if one exists.
- **Shell Reconciliation Record**: The operator-visible record for one shell that links desired generation, applied generation, in-sync or out-of-sync status, reconciliation outcome, and blocking or drift reason.
- **Runtime Feature Catalog**: The current discoverable set of feature definitions assembled from all configured feature assembly providers during a refresh cycle.
- **Candidate Runtime**: A fully built and validated prospective runtime for a target desired shell generation that is not yet serving until atomic commit occurs.
- **Deferred Activation Reason**: The explicit record of which required configured features are currently missing and therefore block application of the latest desired generation.
- **Failure Reason**: The explicit record of a non-deferred candidate-build or validation problem that blocks application of the latest desired generation.

### Assumptions

- Late-loaded assemblies may become available through the same configured feature assembly providers that participate in initial discovery, so refresh must re-evaluate providers rather than relying on one-time discovery.
- Missing required features are treated as a recoverable deferred outcome for the latest desired generation rather than as permission to drop configuration or tear down an existing applied runtime.
- Candidate runtimes can be built and validated before they replace an existing applied runtime, enabling atomic commit semantics.
- Partial startup is preferred over all-or-nothing startup when the runtime feature catalog itself is valid and at least one shell can apply successfully.
- A shell may legitimately have no applied runtime even while its desired state exists, and operator tooling needs to show that distinction clearly.
- Explicit optional-feature semantics may be introduced later, but this feature assumes strict required semantics unless configuration says otherwise in a future design.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In startup tests where configured shells have mixed readiness, 100% of shells whose desired generations are buildable become usable, and 100% of shells whose desired generations are not buildable are reported as deferred or failed without silent feature removal.
- **SC-002**: In update tests where an already active shell receives a newer desired generation that is not yet satisfiable, 100% of those shells continue serving their previously applied runtime until a valid successor runtime is ready or the desired state is explicitly changed again.
- **SC-003**: After additional feature packages become discoverable and reconciliation is triggered, 100% of previously deferred desired generations that are now satisfiable are promoted to applied runtime without requiring an application restart.
- **SC-004**: In reconciliation tests where a catalog refresh detects duplicate feature IDs or another catalog-level inconsistency, 100% of such refresh attempts fail before any previously applied shell availability is changed.
- **SC-005**: In routing and endpoint verification after startup and reconciliation, 0 shells without an applied active runtime are routable, and 100% of exposed shell-backed endpoints belong only to committed applied runtimes.
- **SC-006**: In mixed-outcome reconciliation tests, operators can inspect the desired generation, applied generation if any, reconciliation outcome, and blocking or drift reason for 100% of configured shells.
