# Feature Specification: Partial Shell Activation and Atomic Shell Reconciliation

**Feature Branch**: `005-deferred-shell-activation`
**Created**: 2026-04-15
**Revised**: 2026-04-17
**Status**: Draft
**Input**: Revision of the original deferred shell activation spec. Instead of blocking shell activation when configured features are missing from the catalog, shells activate with the features that are available. Missing features are recorded for operator visibility and become available when the user reloads shells after the missing feature assemblies have been added.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Activate Shells With Available Features (Priority: P1)

As an application operator, I want each shell to activate successfully with whatever configured features are currently discoverable, so that a shell is never blocked from serving just because some of its configured features have not yet been loaded.

**Why this priority**: This is the central behavior change. Shells should be usable immediately with their available features, and missing features should be surfaced transparently rather than used as a blocking condition.

**Independent Test**: Configure a shell with three features where two are available and one is missing. Verify that the shell activates with the two available features, reports `ActiveWithMissingFeatures` as its reconciliation outcome, and lists the missing feature in its `MissingFeatures` collection.

**Acceptance Scenarios**:

1. **Given** a shell is configured with features `[A, B, C]` and only `[A, B]` are present in the refreshed feature catalog, **When** reconciliation runs, **Then** the shell activates with features `[A, B]`, the reconciliation outcome is `ActiveWithMissingFeatures`, and `MissingFeatures` contains `[C]`.
2. **Given** a shell is configured with features `[A, B]` and all are present in the feature catalog, **When** reconciliation runs, **Then** the shell activates with all configured features and the reconciliation outcome is `Active` with an empty `MissingFeatures` collection.
3. **Given** a shell is configured with features `[A, B]` where none are present in the feature catalog, **When** reconciliation runs, **Then** the shell activates with no features, the reconciliation outcome is `ActiveWithMissingFeatures`, and `MissingFeatures` contains `[A, B]`.
4. **Given** a shell's candidate runtime fails to build for a reason other than missing features (e.g. a DI wiring error), **When** reconciliation attempts to build the candidate, **Then** the shell is recorded as `Failed`, any existing applied runtime is preserved, and the failure reason is visible to operators.

---

### User Story 2 - Recover Full Feature Set After Reload (Priority: P2)

As an application operator, I want to reload shells after adding new feature assemblies so that previously missing features become available without restarting the application.

**Why this priority**: Partial activation only becomes operationally complete when late-arriving feature assemblies can be picked up through a reload, transitioning shells from `ActiveWithMissingFeatures` to `Active`.

**Independent Test**: Start with a shell that activated with missing features, then make the missing features discoverable, trigger a reload, and confirm that the shell now includes the previously missing features and transitions to `Active`.

**Acceptance Scenarios**:

1. **Given** a shell is currently `ActiveWithMissingFeatures` because feature `C` was not in the catalog, **When** a reload is triggered after the assembly containing `C` has been added, **Then** the catalog refreshes, the shell rebuilds with all configured features including `C`, and the outcome transitions to `Active` with an empty `MissingFeatures` collection.
2. **Given** some shells are `Active` and others are `ActiveWithMissingFeatures`, **When** a full reconciliation runs after a catalog refresh, **Then** each shell is re-evaluated independently: shells that can now satisfy all features transition to `Active`, and shells that still have missing features remain `ActiveWithMissingFeatures` with an updated `MissingFeatures` list.
3. **Given** a catalog refresh discovers duplicate feature IDs from the configured assembly-provider set, **When** reconciliation is requested, **Then** the refresh fails explicitly and all previously applied shell runtimes remain unchanged.

---

### User Story 3 - Expose Clear Status to Operators and Routing (Priority: P3)

As a CShells maintainer, I want routing, endpoints, and operator-visible status to reflect the applied runtime state including which features are missing, so operators understand exactly what each shell is serving and what is still awaited.

**Why this priority**: Operators need to distinguish between a fully activated shell and one running with partial features, and need to see which features are missing so they can take action.

**Independent Test**: Exercise startup with shells in mixed conditions (fully active, active with missing features, failed), then confirm that all shells are routable (except failed ones), that status inspection shows the reconciliation outcome and missing features for each shell, and that endpoint registration works for the features that did load.

**Acceptance Scenarios**:

1. **Given** a shell activated with partial features, **When** endpoint registration runs, **Then** endpoints are registered only for the features that were successfully loaded; no endpoints are registered for missing features.
2. **Given** one or more shells have `Failed` outcomes because their candidate runtimes could not be built, **When** endpoint registration and request resolution are evaluated, **Then** those shells contribute no routable endpoints and no successful resolution outcomes.
3. **Given** startup finishes with a mix of `Active`, `ActiveWithMissingFeatures`, and `Failed` shells, **When** operators inspect shell status, **Then** they can see the desired generation, applied generation, reconciliation outcome, missing features list, and blocking reason for each configured shell.
4. **Given** a shell named `Default` is explicitly configured but fails to build, **When** fallback shell resolution is attempted, **Then** the system does not silently substitute a different configured shell for that explicit default shell.

### Edge Cases

- A catalog refresh discovers the same feature ID from two different late-loaded assemblies; the refresh must fail explicitly before any applied catalog or applied shell runtime is changed.
- A shell is configured with features `[A, B, C]` where only `[A]` is available; the shell activates with `[A]`, then on reload `[B]` becomes available; the shell rebuilds with `[A, B]` and still reports `C` as missing.
- A shell has no available features at all; it activates as `ActiveWithMissingFeatures` with all features listed as missing. It is routable but serves no feature-contributed endpoints.
- A shell's candidate build fails due to a dependency resolution error in available features (not a missing-feature issue); the system records the outcome as `Failed` and preserves any existing applied runtime.
- A configured `Default` shell is `Failed` while other shells are active; fallback resolution must not silently route requests to a different shell that the operator did not explicitly configure as the default.
- A catalog refresh removes a feature required by the latest desired generation of multiple shells while older applied runtimes still exist; each shell must independently rebuild with available features, potentially transitioning from `Active` to `ActiveWithMissingFeatures`.
- No shells have an applied runtime after reconciliation because every configured shell failed; routing must expose no shell-backed endpoints and operator status must report the system as configured but currently unapplied.

## Two-Layer Shell Truth Model

### Per-Shell Sources of Truth

| Layer | Meaning | Update rule |
| --- | --- | --- |
| Configured desired state | The latest configured shell definition, including all configured features and configuration, regardless of which features are currently available. | It updates whenever configuration providers publish a newer shell definition. |
| Applied runtime state | The last committed runtime for that shell that is currently serving traffic, if one exists. The applied runtime may include only a subset of the configured features if some were missing from the catalog at build time. | It changes only after a candidate runtime for a target desired generation has been fully built and atomically committed. |

### Reconciliation Outcome Vocabulary

| Outcome | Meaning | Availability rules |
| --- | --- | --- |
| `Active` | The applied runtime exists, is currently serving, and includes all configured features. | The applied active runtime may be resolved, expose endpoints, and participate in active lifecycle behavior. |
| `ActiveWithMissingFeatures` | The applied runtime exists and is currently serving, but one or more configured features were not available in the catalog at build time. The shell is fully operational for its loaded features. | The applied active runtime may be resolved, expose endpoints for loaded features, and participate in active lifecycle behavior. Missing features contribute no endpoints or services. |
| `Failed` | The latest desired generation could not be built for a reason other than missing features (e.g. DI wiring error, circular dependency). | If an older applied runtime exists, it remains active until a later desired generation can be committed; otherwise the shell has no applied runtime and is unavailable. |

### Required Operator Visibility Per Shell

Operators and runtime components must be able to inspect, for each configured shell, the latest desired generation, the currently applied generation if any, whether those generations are in sync, the reconciliation outcome for the latest desired generation, the list of missing features if any, and the blocking or failure reason when the desired generation could not be fully applied.

## Current Architecture Interaction Analysis

| Component | Current role in CShells | Required behavior direction for this feature |
| --- | --- | --- |
| `DefaultShellHost` | Discovers features, validates configured features, and lazily builds `ShellContext` instances from `ShellSettings`. | When building a candidate, it must filter the configured feature set to those present in the catalog, build the shell with available features only, and pass the missing feature list through to the candidate result. |
| `DefaultShellManager` | Reloads one shell or all shells from provider data and evicts or rebuilds runtime contexts. | It must refresh the feature catalog before evaluation, build candidates that activate with available features, and commit candidates even when some features are missing. The `MarkDeferred` path is replaced by committing with `ActiveWithMissingFeatures` outcome. |
| `ShellStartupHostedService` | Eagerly activates all shells at startup and publishes startup lifecycle notifications. | It must support partial feature loading by activating shells with available features and recording missing features, rather than blocking activation. |
| `ShellEndpointRegistrationHandler` | Registers or removes shell endpoints in response to shell lifecycle notifications. | No change needed. It already only registers endpoints for features present in the `ShellContext.EnabledFeatures` list, which will now contain only the features that were actually loaded. |
| Feature assembly providers | Supply assemblies used for feature discovery, including host and explicit assembly sources. | No change. They must still participate in repeatable catalog refresh cycles. |
| Resolver behavior, including `DefaultShellResolverStrategy` | Chooses which shell should handle a request, with a fallback that currently resolves `Default`. | Resolution must consult only applied active runtimes (both `Active` and `ActiveWithMissingFeatures` shells are routable). If `Default` is explicitly configured but `Failed`, the system must report it as unavailable rather than silently substituting another shell. |

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a shell is configured with features that are not present in the current runtime feature catalog, the system MUST activate the shell with the features that ARE available and MUST record the missing feature IDs on the shell's runtime status and context.
- **FR-002**: The system MUST maintain, for every configured shell, a configured desired state that is authoritative even when the shell is running with only a subset of its configured features.
- **FR-003**: The system MUST maintain, separately from desired state, an applied runtime state representing the last committed runtime that is currently safe to serve, if any.
- **FR-004**: The system MUST expose whether each shell's desired state and applied runtime state are in sync, and which configured features are currently missing from the applied runtime.
- **FR-005**: When a shell activates with all configured features present, the system MUST record the reconciliation outcome as `Active`. When a shell activates with one or more configured features missing, the system MUST record the outcome as `ActiveWithMissingFeatures` and populate the `MissingFeatures` collection.
- **FR-006**: When a shell has an existing applied runtime and a newer desired generation is reconciled, the system MUST build a candidate with available features and atomically swap to the new runtime. The previous runtime MUST remain in service until the new candidate is fully ready.
- **FR-007**: If a desired shell generation cannot be built for a reason other than missing required features (e.g. DI errors, circular dependencies), the system MUST record that desired generation as `Failed` and MUST preserve any existing applied runtime for that shell until a valid successor is ready or the shell is explicitly removed from desired state.
- **FR-008**: The system MUST retain the failure reason for each failed desired generation so operators can diagnose the issue.
- **FR-009**: The system MUST provide a refreshable runtime feature catalog that can re-evaluate all configured feature assembly providers multiple times during the application lifetime.
- **FR-010**: Every shell reload or shell-set reconciliation operation MUST refresh the runtime feature catalog before evaluating and mutating shell runtime state.
- **FR-011**: If a catalog refresh detects duplicate feature IDs or any other catalog-level inconsistency, the refresh MUST fail explicitly and MUST leave the previously applied catalog and all previously applied shell runtimes unchanged.
- **FR-012**: For any shell whose desired generation is eligible to advance, the system MUST build and validate a candidate runtime before changing the currently applied runtime for that shell.
- **FR-013**: Reconciliation MUST commit a shell runtime update atomically: the system MUST keep the current applied runtime serving until the successor runtime for the target desired generation is fully ready, and the system MUST swap from old applied runtime to new applied runtime as one logical commit.
- **FR-014**: Once a shell is `ActiveWithMissingFeatures` and a later catalog refresh makes the missing features available, the system MUST build a candidate runtime with all configured features and promote it to the applied runtime, transitioning the outcome from `ActiveWithMissingFeatures` to `Active`.
- **FR-015**: Startup MUST support partial feature loading by activating every buildable shell with its available features, recording missing features, and leaving only genuinely failed shells without an applied runtime.
- **FR-016**: Shell reconciliation semantics MUST apply to every configured shell, not only to `Default`, although the spec MUST retain explicit resolver behavior for an explicitly configured `Default` shell.
- **FR-017**: If a shell named `Default` is explicitly configured but does not currently have an applied runtime (i.e. it is `Failed`), default-shell resolution MUST report that shell as unavailable and MUST NOT silently substitute a different configured shell.
- **FR-018**: If no shell named `Default` is configured, any fallback default-shell selection behavior MUST choose only from shells that currently have an applied active runtime.
- **FR-019**: Only shells with an applied active runtime (both `Active` and `ActiveWithMissingFeatures`) MUST be eligible for request resolution, route registration, endpoint exposure, and active-shell enumeration used by the web runtime.
- **FR-020**: Endpoint-registration behavior MUST respond to atomic shell commits so newly applied runtimes gain endpoints only after commit, and shells that lose their applied runtime through removal or explicit deactivation lose endpoints before reconciliation is considered complete.
- **FR-021**: Lifecycle behavior tied to shell activation and deactivation MUST apply only to changes in applied active runtime state; recording a new desired generation that has not yet been committed MUST NOT be treated as a completed activation.
- **FR-022**: The system MUST expose, for every configured shell, the desired generation, applied generation if any, reconciliation outcome (`Active`, `ActiveWithMissingFeatures`, or `Failed`), missing features list, in-sync or out-of-sync status, and blocking or failure reason so operators can inspect shell state.
- **FR-023**: A configured shell MUST remain part of desired state until it is removed from shell configuration; partial feature activation MUST NOT imply silent deletion of the shell or its missing features from desired state.
- **FR-024**: Runtime management behavior for one-shell reloads and full-shell reconciliation MUST use the same catalog-refresh-first, candidate-build, and atomic-commit semantics.
- **FR-025**: The `ShellContext` MUST carry a `MissingFeatures` collection so that code running within the shell can inspect which configured features were not loaded.
- **FR-026**: The design for this feature MAY introduce breaking changes to existing public contracts and internal architecture where needed to support partial activation, atomic runtime replacement, and refreshable catalog behavior.

### Key Entities *(include if feature involves data)*

- **Configured Desired Shell Generation**: The latest configured version of a shell, including all its configured features and configuration, regardless of which features are currently available in the catalog.
- **Applied Shell Runtime**: The last committed runtime instance for a shell that is currently serving traffic, if one exists. May include only a subset of configured features.
- **Shell Reconciliation Record**: The operator-visible record for one shell that links desired generation, applied generation, in-sync or out-of-sync status, reconciliation outcome, missing features, and failure reason.
- **Runtime Feature Catalog**: The current discoverable set of feature definitions assembled from all configured feature assembly providers during a refresh cycle.
- **Candidate Runtime**: A fully built and validated prospective runtime for a target desired shell generation that is not yet serving until atomic commit occurs. Built with available features only.
- **Missing Features List**: The list of configured feature IDs that were not present in the catalog when the shell was last built. Exposed on both `ShellContext` and `ShellRuntimeStatus`.

### Assumptions

- Late-loaded assemblies may become available through the same configured feature assembly providers that participate in initial discovery, so refresh must re-evaluate providers rather than relying on one-time discovery.
- Missing features are treated as a non-blocking condition: the shell activates with available features and records what is missing.
- Candidate runtimes can be built and validated before they replace an existing applied runtime, enabling atomic commit semantics.
- Partial startup is preferred over all-or-nothing startup when the runtime feature catalog itself is valid and at least one shell can build successfully.
- A shell with missing features is still routable and participates in resolution, endpoint registration (for its loaded features), and lifecycle behavior.
- On reload, the catalog refreshes and shells rebuild with whatever features are now available, potentially transitioning from `ActiveWithMissingFeatures` to `Active`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In startup tests where configured shells have mixed feature availability, 100% of buildable shells become routable with their available features, and missing features are recorded on each shell's status without blocking activation.
- **SC-002**: In update tests where an active shell receives a newer desired generation, the shell atomically rebuilds with available features, and any newly missing or newly available features are reflected in the updated status.
- **SC-003**: After additional feature packages become discoverable and reconciliation is triggered, 100% of previously `ActiveWithMissingFeatures` shells that can now satisfy all configured features transition to `Active` without requiring an application restart.
- **SC-004**: In reconciliation tests where a catalog refresh detects duplicate feature IDs or another catalog-level inconsistency, 100% of such refresh attempts fail before any previously applied shell availability is changed.
- **SC-005**: In routing and endpoint verification after startup and reconciliation, 0 `Failed` shells are routable, and 100% of exposed shell-backed endpoints belong only to committed applied runtimes (`Active` or `ActiveWithMissingFeatures`).
- **SC-006**: In mixed-outcome reconciliation tests, operators can inspect the desired generation, applied generation, reconciliation outcome, missing features list, and failure reason for 100% of configured shells.
