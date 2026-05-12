# Research: Lifecycle Ordering

## Decision: Treat feature dependency order and lifecycle order as separate contracts

**Rationale**: `DependsOn` already means dependency features configure services before dependent features. Reusing or reversing it for initializer execution would break existing mental models and could make service configuration invalid. Lifecycle order should be declared by lifecycle registrations or metadata and applied only when activation work runs.

**Alternatives considered**:

- Reverse feature dependency order for initializers: rejected because it would surprise existing features and fails cases where dependency and lifecycle order should be the same.
- Add more feature dependency kinds: rejected because lifecycle work is registered per component, not always one-to-one with features.
- Rely on DI registration order: rejected because dependency-first feature configuration can force the wrong initializer order.

## Decision: Add transient `AddShellInitializer<TInitializer>(...)` registration APIs

**Rationale**: Feature authors already register lifecycle components inside `ConfigureServices`. A first-class extension method keeps the authoring model familiar, avoids manual descriptor replacement, and stores explicit ordering metadata alongside the `IShellInitializer` registration. Transient lifetime matches existing initializer guidance and gives each activation scope a fresh initializer instance.

**Alternatives considered**:

- Attribute-only ordering: useful for reusable types, but insufficient when the same initializer type needs different order in different integrations.
- Scoped or singleton default lifetime: rejected because current guidance says initializers are transient and activation should not share initializer instances unexpectedly.
- Lifetime overloads: rejected for initial scope because they expand the API without a concrete requirement.
- Manual `IServiceCollection.Replace` or descriptor editing: rejected by the spec and by current `IShellInitializer` guidance.

## Decision: Support optional initializer type metadata, overridden by explicit registration metadata

**Rationale**: Attributes make reusable initializer types self-documenting and are easy for package authors. Registration metadata must win because the registration site has the most context and because one type may be reused in different positions. Attribute reading can happen from `Type` metadata without constructing shell-scoped services early.

**Alternatives considered**:

- No attributes: simpler, but misses the requested concise authoring option and forces repeated order arguments for reusable initializer types.
- Attribute metadata always wins: rejected because it prevents host or provider-specific overrides.

## Decision: Use semantic phases plus numeric order only

**Rationale**: Numeric order is simple to sort and test. Named phases provide semantic guidance for common cases such as preparation before startup without requiring authors to memorize values. This covers the Quartz-style scenario without introducing graph ordering complexity.

**Alternatives considered**:

- Before/after relationship graph: rejected for this feature because the clarified requirement excludes relationship graphs and because graph diagnostics are not needed for the motivating scenario.
- Phase-only ordering: too coarse when two initializers inside the same phase need deterministic order.
- General lifecycle pipeline middleware: far more complexity than the requirements need.

## Decision: Place unordered initializers in `Default` between `Prepare` and `Start`

**Rationale**: Existing `services.AddTransient<IShellInitializer, T>()` registrations continue to run in DI registration order relative to one another. Placing them in `Default` gives provider preparation work an obvious earlier phase and base runtime startup work an obvious later phase, while preserving legacy behavior when only unordered initializers are present.

**Alternatives considered**:

- Move unordered initializers before all explicit phases: rejected because it prevents provider preparation work from reliably running first.
- Move unordered initializers after all explicit phases: rejected because it can delay existing startup work past new runtime-start initializers.
- Preserve pure DI position among explicit registrations: rejected because explicit phase/order declarations should not depend on descriptor interleaving.

## Decision: Validate invalid ordering metadata before running initializers

**Rationale**: Initialization side effects such as migrations and scheduler startup should not partially run if lifecycle metadata is invalid. Invalid type metadata, mismatched registrations, and unsupported metadata should fail activation before any initializer is invoked, with messages that include the shell descriptor and initializer types. Equal phase/order values are valid with a deterministic registration-index tie-break and may be logged as ambiguity rather than failing.

**Alternatives considered**:

- Best-effort sorting with warnings only: rejected because invalid metadata can cause unsafe startup side effects.
- Fail on every equal order: rejected because shared phases and equal defaults are common and deterministic tie-breaks are sufficient.

## Decision: Keep drain handlers parallel and defer ordered drain execution

**Rationale**: Drain currently has a concurrency contract: handlers are invoked in parallel under a deadline and grace period. Changing that default would risk shutdown performance and violate existing tests. Ordered or phased drain execution is explicitly out of scope for this feature and should require a separate design.

**Alternatives considered**:

- Apply the same sequential ordering model to all drain handlers: rejected because it changes default behavior and complicates deadline handling.
- Add opt-in ordered drain now: rejected because the clarified scope defers ordered/phased drain execution.
- Ignore drain documentation: rejected because the spec requires documenting the deferred boundary.

## Decision: Document provider/base feature pairs as dependency-for-configuration plus lifecycle-order-for-startup

**Rationale**: Provider features often depend on base features for registrations but must run preparation before the base feature starts runtime work. Documentation should show this explicitly so authors do not reverse `DependsOn` or force shell-scoped services into constructors.

**Alternatives considered**:

- Ask base features to know about every provider: rejected because it creates direct coupling.
- Ask providers to start the base feature manually: rejected because lifecycle work should remain in the registry-controlled initializer sequence.
