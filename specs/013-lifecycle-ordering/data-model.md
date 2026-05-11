# Data Model: Lifecycle Ordering

## Lifecycle Component

Represents a shell-scoped lifecycle participant.

**Fields**:

- `ServiceType`: Lifecycle interface, such as `IShellInitializer` or `IDrainHandler`.
- `ImplementationType`: Concrete component type resolved from the shell provider.
- `RegistrationIndex`: Stable index based on service registration order inside the shell service collection.
- `Source`: Human-readable origin used in diagnostics, such as explicit registration API, attribute metadata, or legacy DI registration.

**Relationships**:

- Initializer components participate in shell activation and may have lifecycle ordering metadata.
- Drain handler components participate in shell drain and remain parallel; this feature does not add ordered or phased drain execution.

**Validation Rules**:

- `ServiceType` and `ImplementationType` must be non-null.
- Initializer implementation types must be assignable to `IShellInitializer`.
- Drain handler behavior must remain compatible with current parallel execution.
- Duplicate component entries are allowed only when the author intentionally registered multiple lifecycle entries.

## Shell Initializer Registration

Represents a transient initializer registration plus metadata used to plan execution order.

**Fields**:

- `InitializerType`: The concrete initializer implementation type.
- `Order`: Numeric execution order within a phase.
- `Phase`: Semantic lifecycle phase.
- `RegistrationIndex`: Stable tie-break preserving service registration order.
- `IsExplicit`: Whether ordering metadata came from a first-class lifecycle registration API.
- `AttributeMetadata`: Ordering metadata discovered on the initializer type, if present.
- `Source`: Diagnostic description of the registration site or metadata source.
- `Lifetime`: Always transient for `AddShellInitializer<TInitializer>()` registrations.

**Relationships**:

- Resolves an `IShellInitializer` instance from the shell provider at execution time.
- May override ordering metadata declared on the initializer type.
- Belongs to one shell activation plan.

**Validation Rules**:

- Explicit registration metadata overrides attribute metadata.
- If neither explicit metadata nor attribute metadata is present, the registration is unordered and uses the `Default` phase.
- Multiple registrations of the same initializer type are treated as distinct lifecycle entries when they came from distinct service registrations.
- Planning errors must be detected before any initializer runs.

## Lifecycle Phase

Represents the semantic bucket used to compare initializer order.

**Values**:

- `Prepare`: Runs before unordered compatibility initializers and before runtime startup work.
- `Default`: Compatibility phase for unordered initializers; runs after `Prepare` and before `Start`.
- `Start`: Runs after `Prepare` and `Default`.

**Validation Rules**:

- Phase ordering is deterministic: `Prepare` → `Default` → `Start`.
- Additional phase values, if any, must have documented ordering relative to these required phases.

## Lifecycle Order

Represents the effective ordering value used by the activation planner.

**Fields**:

- `Phase`: Required semantic bucket.
- `Order`: Numeric order within the phase.
- `TieBreakIndex`: Registration index used for deterministic ordering when phase/order are equal.
- `IsDefaultCompatibilityOrder`: Whether the value was assigned because no explicit metadata was present.

**Relationships**:

- Computed from shell initializer registration metadata.
- Used by the initializer ordering planner to produce an execution list.

**Validation Rules**:

- Lower phase position runs earlier.
- Within the same phase, lower numeric order runs earlier.
- Equal phase/order is valid when registration-index tie-break is available.
- Before/after relationship declarations are not supported by this feature.

## Lifecycle Ordering Plan

Represents the ordered sequence of initializers for one shell activation.

**Fields**:

- `ShellDescriptor`: Shell identity and generation being activated.
- `Entries`: Ordered initializer entries.
- `Diagnostics`: Non-fatal ambiguity information, if any.

**Relationships**:

- Built from shell provider service metadata before initializer invocation.
- Consumed by `ShellRegistry` during the Initializing to Active transition.

**Validation Rules**:

- Invalid metadata fails plan creation.
- No initializer side effects may occur before the plan is valid.
- Equal phase/order entries must be sorted deterministically by registration index.

## Ordering Diagnostic

Represents actionable feedback about lifecycle ordering.

**Fields**:

- `Severity`: Warning or error.
- `ShellDescriptor`: Shell identity affected by the diagnostic.
- `ComponentTypes`: Lifecycle component types involved.
- `Message`: Actionable explanation.
- `Source`: Registration or metadata source when available.

**Relationships**:

- Error diagnostics fail activation before initializers run.
- Warning diagnostics may be logged while preserving deterministic execution.

**Validation Rules**:

- Error diagnostics must include enough type/source information to fix the registration.
- Diagnostics must not require constructing initializer instances before ordering is planned.

## Provider/Base Feature Pair

Represents an integration pattern where a provider feature depends on a base feature for service configuration but needs earlier lifecycle work.

**Fields**:

- `BaseFeatureName`: Feature that configures shared/base services.
- `ProviderFeatureName`: Feature that depends on the base feature.
- `ProviderPreparationInitializer`: Initializer in `Prepare`.
- `BaseStartupInitializer`: Initializer in `Start`.

**Relationships**:

- Provider feature keeps `DependsOn` pointing to the base feature.
- Provider preparation initializer declares an earlier lifecycle phase/order than base startup initializer.

**Validation Rules**:

- Feature dependency order must remain dependencies-first.
- Lifecycle order must not be inferred from feature dependency direction.
