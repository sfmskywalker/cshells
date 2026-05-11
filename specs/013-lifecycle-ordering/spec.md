# Feature Specification: Lifecycle Ordering

**Feature Branch**: `013-lifecycle-ordering`  
**Created**: 2026-05-11  
**Status**: Draft  
**Input**: User description: "Specify a CShells feature-lifecycle ordering improvement. CShells currently topologically sorts shell features by ShellFeatureAttribute.DependsOn and calls IShellFeature.ConfigureServices in dependencies-first order. After the shell service provider is built, ShellRegistry resolves IShellInitializer from DI and runs them sequentially in DI registration order. IDrainHandler instances run in parallel during drain. Feature dependency order is not expressive enough for lifecycle work. A feature can depend on another feature for service configuration, yet need its initializer to run before the dependency's initializer. Example: QuartzPostgreSqlFeature depends on QuartzFeature, so QuartzFeature configures first. But QuartzPostgreSqlFeature registers an EF migration initializer that must run before QuartzFeature's scheduler-start initializer. With pure DI registration order, Quartz starts before schema migrations run. Design a first-class CShells API that lets feature authors express lifecycle ordering independently from feature configuration ordering."

## Clarifications

### Session 2026-05-11

- Q: Should this feature implement ordered/phased drain execution or defer it? → A: Defer ordered/phased drain execution; preserve current parallel drain behavior and document future extension only.
- Q: Should initializer ordering support before/after relationship graphs in this feature? → A: Use numeric order plus semantic phases only; no before/after relationship graph in this feature.
- Q: Where should unordered initializers execute relative to explicit phases? → A: Unordered initializers run in the `Default` phase between `Prepare` and `Start`, preserving DI registration order within that phase.
- Q: What service lifetime should `AddShellInitializer<T>()` use? → A: `AddShellInitializer<T>()` registers `T` as transient.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Order Initializers Independently Of Feature Dependencies (Priority: P1)

As a feature author, I want to declare when my shell initializer runs without changing feature dependency order so service configuration remains dependencies-first while lifecycle work can follow the operational sequence the feature requires.

**Why this priority**: This is the core gap. Some features must configure after their dependency but initialize before it, and the current DI registration order cannot express that safely.

**Independent Test**: Can be fully tested with two enabled features where the dependent feature configures after its dependency but declares an initializer that must execute first; shell activation proves configuration order and initializer order are independent.

**Acceptance Scenarios**:

1. **Given** Feature B depends on Feature A for configuration, **When** both features register shell initializers and Feature B declares an earlier lifecycle order, **Then** Feature A still configures before Feature B while Feature B's initializer runs before Feature A's initializer.
2. **Given** a shell has ordered and unordered initializer registrations, **When** the shell activates, **Then** ordered initializers run in deterministic phase/order sequence and unordered initializers run in the `Default` phase while preserving their existing DI registration behavior relative to each other.
3. **Given** a feature declares lifecycle order for an initializer, **When** activation fails inside that initializer, **Then** activation still fails with the initializer error and does not promote the shell to active.

---

### User Story 2 - Keep Existing Initializer Behavior Compatible (Priority: P2)

As an application maintainer, I want existing initializer registrations to behave the same after upgrading so hosts and features that do not opt into lifecycle ordering are not reordered unexpectedly.

**Why this priority**: Initializers often perform startup side effects. Backward-compatible defaults prevent subtle activation regressions in existing applications.

**Independent Test**: Can be fully tested by activating a shell whose features register multiple initializers using the current unqualified pattern and verifying the observed order remains DI registration order.

**Acceptance Scenarios**:

1. **Given** existing features register initializers without explicit lifecycle order, **When** the shell activates, **Then** their execution order is the same as before the feature.
2. **Given** an existing initializer has no ordering metadata, **When** ordered initializers are also present, **Then** the existing initializer participates in the `Default` phase between `Prepare` and `Start` and keeps stable ordering against other unordered initializers.
3. **Given** a host uses existing feature dependencies only, **When** shell activation runs, **Then** dependency ordering still affects service configuration only and is not reinterpreted as lifecycle ordering.

---

### User Story 3 - Offer Simple Authoring Options (Priority: P3)

As a feature author, I want a concise registration model such as an ordered initializer registration, an initializer attribute, or semantic phases so I can express lifecycle intent without manually replacing service registrations.

**Why this priority**: The feature must be easy to adopt inside normal feature `ConfigureServices` methods. If authors must manipulate descriptors manually, the API will be fragile and inconsistent.

**Independent Test**: Can be fully tested by registering initializers through the supported authoring mechanisms and verifying each mechanism contributes the expected lifecycle order without duplicate registrations or manual service replacement.

**Acceptance Scenarios**:

1. **Given** a feature uses an ordered initializer registration, **When** the shell activates, **Then** the initializer runs at the declared order while remaining resolvable through the shell's service provider.
2. **Given** a feature uses supported ordering metadata on the initializer type, **When** the shell activates, **Then** the metadata is honored unless an explicit registration order overrides it.
3. **Given** a feature author follows the documented API, **When** multiple lifecycle registrations are present, **Then** the author does not need to remove, replace, or reorder raw service descriptors manually, and ordered initializer implementations are registered with transient lifetime.

---

### User Story 4 - Diagnose Ambiguous Lifecycle Ordering (Priority: P4)

As a host author, I want actionable diagnostics when lifecycle order cannot be determined clearly so misconfigured features fail or warn before producing surprising startup behavior.

**Why this priority**: Lifecycle ordering controls startup side effects such as migrations and scheduler start. Ambiguous or invalid declarations should be visible during activation troubleshooting.

**Independent Test**: Can be fully tested by defining invalid lifecycle metadata and equal-order lifecycle declarations and verifying diagnostics identify the affected lifecycle component types and ordering declarations.

**Acceptance Scenarios**:

1. **Given** an initializer declares invalid lifecycle ordering metadata, **When** the shell activates, **Then** activation fails before running initializers and the diagnostic names the affected initializer.
2. **Given** two initializers have the same explicit phase and order, **When** the shell activates, **Then** their tie-break behavior is deterministic and documented.
3. **Given** lifecycle ordering metadata cannot be matched to a valid initializer registration, **When** the shell activates, **Then** the system reports actionable diagnostic information without requiring feature constructors to consume shell-scoped services.

---

### User Story 5 - Preserve Parallel Drain Behavior (Priority: P5)

As a feature author, I want drain behavior to remain parallel so current shutdown performance and compatibility are preserved while ordered or phased drain execution remains a documented future extension point.

**Why this priority**: Drain handlers are designed to run concurrently today. Initializer ordering is the urgent problem, but the lifecycle model should leave room for ordered drain work without changing existing drain behavior.

**Independent Test**: Can be fully tested by registering multiple drain handlers and confirming they still run in parallel, with no ordered or phased drain execution introduced by this feature.

**Acceptance Scenarios**:

1. **Given** existing drain handlers, **When** a shell drains, **Then** handlers continue to run in parallel.
2. **Given** initializer lifecycle ordering is enabled for a shell, **When** that shell later drains, **Then** drain deadlines, force-drain cancellation, and handler result reporting remain unchanged.
3. **Given** a feature author reads drain lifecycle guidance, **When** they need ordered drain work, **Then** the documentation states that ordered or phased drain execution is deferred and not part of this feature.

---

### User Story 6 - Guide Provider And Base Feature Pairs (Priority: P6)

As an integration package author, I want documentation for provider/base feature pairs so provider features can perform preparation work before base feature startup work while still depending on the base feature for service configuration.

**Why this priority**: Provider/base pairs such as storage providers and schedulers are the motivating case. Clear guidance prevents authors from misusing feature dependencies to control lifecycle order.

**Independent Test**: Can be fully tested by reviewing documentation and a Quartz-style example that shows a provider feature depending on a base feature for configuration while its migration initializer runs before the base scheduler initializer.

**Acceptance Scenarios**:

1. **Given** a provider feature depends on a base feature, **When** the provider needs preparation work to run before the base feature starts, **Then** documentation shows declaring initializer order instead of reversing feature dependencies.
2. **Given** a Quartz-style provider registers a migration initializer and the base feature registers a scheduler-start initializer, **When** a shell activates, **Then** migrations complete before scheduler start while Quartz services are still configured first.
3. **Given** a feature author reads lifecycle guidance, **When** they choose between feature dependency and lifecycle ordering, **Then** the documentation clearly states that dependency order controls configuration and lifecycle order controls runtime lifecycle work.

### Edge Cases

- A shell with no initializers still activates successfully.
- A shell with only unordered initializers treats them as `Default` phase entries and preserves existing DI registration order.
- A shell with a mix of explicit orders, ordering metadata, and unordered initializers has a deterministic and documented execution sequence.
- Multiple initializers with the same explicit order must have a deterministic tie-break that does not depend on reflection enumeration randomness.
- Before/after relationship declarations are not part of this feature; initializer ordering uses semantic phases plus numeric order only.
- Invalid lifecycle ordering metadata must fail before any initializer runs.
- Ordering metadata on an initializer type must not require constructing that initializer before the shell service provider is available.
- Ordered initializer registrations must register initializer implementations with transient lifetime.
- Open generic, keyed, decorator, or duplicate registrations must not cause duplicate initializer executions unless the author intentionally registered multiple lifecycle entries.
- An initializer resolved from DI that does not match its lifecycle registration must produce actionable diagnostics rather than silently ignoring order metadata.
- Drain handlers must keep running in parallel.
- Ordered or phased drain execution is deferred and must not be introduced as part of this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST preserve existing feature dependency semantics: feature dependencies continue to mean that dependency features configure services before dependent features.
- **FR-002**: The system MUST provide a first-class way for feature authors to declare shell initializer execution order independently from feature configuration order.
- **FR-003**: The system MUST preserve current initializer behavior by default for existing initializer registrations that do not use explicit lifecycle ordering.
- **FR-004**: The system MUST execute shell initializers deterministically during shell activation.
- **FR-005**: The system MUST support a simple ordered initializer authoring model suitable for use from a feature's service configuration method, such as `AddShellInitializer<TInitializer>(order)`.
- **FR-006**: The system SHOULD support initializer type metadata, such as an ordering attribute, as a concise authoring option when it can be honored without constructing the initializer early.
- **FR-007**: The system MUST support semantic lifecycle phases for initializers alongside numeric order.
- **FR-008**: The system MUST assign unordered initializers to the `Default` phase, which executes after `Prepare` and before `Start`.
- **FR-009**: The system MUST keep relative execution order stable for unordered initializers that currently rely on DI registration order.
- **FR-010**: The system MUST define deterministic tie-break behavior for initializers with the same explicit order or phase.
- **FR-011**: The system MUST NOT introduce before/after relationship declarations for initializer ordering in this feature.
- **FR-012**: The system MUST detect invalid initializer ordering metadata before running any initializer for that activation.
- **FR-013**: Diagnostics for ambiguous or invalid lifecycle ordering MUST identify the affected initializer types and the shell being activated.
- **FR-014**: The lifecycle ordering model MUST NOT require feature constructors to consume shell-scoped services.
- **FR-015**: Feature authors MUST NOT need to manually replace, remove, or reorder raw service descriptors to use lifecycle ordering.
- **FR-016**: Ordered initializers MUST still be resolved from the shell service provider so they can consume shell-scoped services at execution time.
- **FR-017**: An initializer failure MUST continue to abort shell activation and prevent the shell from being promoted to active.
- **FR-018**: `AddShellInitializer<TInitializer>()` registrations MUST register initializer implementations with transient lifetime.
- **FR-019**: The system MUST include a Quartz-style scenario proving a provider feature can depend on a base feature for service configuration while its preparation initializer runs before the base feature's startup initializer.
- **FR-020**: The system MUST include tests proving dependency order and initializer order are independent.
- **FR-021**: The system MUST include tests proving current unordered initializer behavior remains compatible.
- **FR-022**: The system MUST include tests proving explicit initializer order is honored deterministically.
- **FR-023**: The system MUST preserve current parallel drain behavior for drain handlers.
- **FR-024**: The system MUST NOT introduce ordered or phased drain execution as part of this feature.
- **FR-025**: Documentation MUST state that ordered or phased drain execution is deferred and would require a separate design for drain deadlines, force-drain cancellation, handler errors, and drain result reporting.
- **FR-026**: Documentation MUST explain that feature dependency order controls service configuration and lifecycle ordering controls activation or drain work.
- **FR-027**: Documentation MUST show provider/base feature guidance, including the provider feature depending on the base feature for configuration while declaring earlier initializer execution for provider preparation work.
- **FR-028**: Documentation MUST include the recommended authoring API and at least one example using explicit initializer order.

### Key Entities *(include if feature involves data)*

- **Feature Dependency Order**: The dependencies-first order used to call enabled features' service configuration methods.
- **Lifecycle Component**: A shell-scoped lifecycle participant; this feature applies ordering to shell initializers only and preserves parallel drain handlers.
- **Shell Initializer Registration**: A transient registration that identifies an initializer type plus optional lifecycle ordering metadata.
- **Lifecycle Order**: A deterministic semantic phase plus numeric ordering value that controls when a shell initializer runs relative to others.
- **Lifecycle Phase**: A named lifecycle bucket that communicates semantic intent such as preparation before startup.
- **Unordered Initializer**: An existing initializer registration with no explicit lifecycle ordering metadata.
- **Ordering Diagnostic**: A warning or activation failure that identifies ambiguous, invalid, or missing lifecycle ordering declarations.
- **Provider/Base Feature Pair**: A pair where a provider feature depends on a base feature for service configuration but may need provider lifecycle work to run before base lifecycle work.

### Assumptions

- Initializer ordering is the required first-class capability for this feature.
- Numeric order and semantic phases are the ordering model for this feature; before/after relationship graphs are out of scope.
- Unordered initializers occupy the `Default` phase between `Prepare` and `Start` and preserve DI registration order against other unordered initializers.
- Ordered or phased drain execution is deferred; this feature preserves current parallel drain behavior.
- Diagnostics may be implemented as activation failures for invalid ordering metadata and log messages or inspectable metadata for non-fatal ambiguity.
- The QuartzPostgreSqlFeature and QuartzFeature scenario is representative of provider/base integrations that need separate configuration and lifecycle ordering.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of dependency-versus-initializer-order tests show service configuration remains dependencies-first while initializer execution follows explicit lifecycle order.
- **SC-002**: 100% of compatibility tests for unordered initializers observe the same relative initializer order as current DI registration order.
- **SC-003**: 100% of explicit initializer order tests execute initializers deterministically across repeated activations.
- **SC-004**: The Quartz-style test proves the provider migration initializer completes before the base scheduler-start initializer while the provider feature still depends on the base feature.
- **SC-005**: Invalid initializer ordering metadata tests fail before any initializer side effects are recorded and include the affected initializer type names in diagnostics.
- **SC-006**: Existing parallel-drain tests continue to prove drain handlers run concurrently and are unaffected by initializer lifecycle ordering.
- **SC-007**: Documentation includes at least one ordered initializer registration example, one lifecycle phase example, and one provider/base feature example.
- **SC-008**: Feature authors can adopt ordered initializer registration in a feature configuration method without writing manual service descriptor replacement logic.
