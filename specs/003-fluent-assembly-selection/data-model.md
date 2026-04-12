# Data Model: Fluent Assembly Source Selection

## Feature Assembly Provider Interface

Represents one discovery-source contract capable of contributing assemblies for feature scanning.

### Fields

- `ProviderType`: the concrete provider implementation type used at runtime
- `ResolutionBehavior`: logic that returns zero or more assemblies for feature discovery
- `ResolutionContext`: the root application service provider passed into provider evaluation
- `Origin`: whether the provider is built-in host-derived, built-in explicit, or developer-supplied custom

### Validation Rules

- Public implementations must satisfy the `IFeatureAssemblyProvider` contract defined in `CShells.Abstractions`.
- Provider instances added through the fluent builder must not be `null`.
- Provider outputs may be empty, but they must not be `null` and they must not re-enable implicit host-derived scanning.
- Provider evaluation always receives the root application service provider used during shell registration.

## Builder Assembly Provider Registration

Represents one appended provider entry in the `CShellsBuilder`-managed list.

### Fields

- `RegistrationOrder`: the call-order index of the fluent assembly-source invocation
- `ProviderFactoryOrInstance`: the provider registration payload appended by the builder API
- `SourceKind`: `Host`, `ExplicitAssemblies`, or `CustomProvider`
- `ActivatesExplicitMode`: whether the registration counts toward explicit-source mode

### Validation Rules

- Every assembly-source fluent call appends at least one registration entry.
- Existing registration entries are never replaced by later calls.
- Empty explicit assembly inputs still append a registration and therefore activate explicit-source mode.
- Call order is preserved for later aggregation and diagnostics.

## Assembly Discovery Mode

Represents how CShells decides which provider set drives feature discovery for the application.

### States

- `ImplicitHostDefault`: used only when no assembly-source fluent calls were made
- `ExplicitProviders`: used once any assembly-source fluent call appends a provider registration

### State Transitions

- `ImplicitHostDefault` → `ExplicitProviders`: triggered by `FromAssemblies(...)`, `FromHostAssemblies()`, or `WithAssemblyProvider(...)`
- `ImplicitHostDefault` → `ImplicitHostDefault`: maintained when no assembly-source API is called
- `ExplicitProviders` has no transition back during the same builder configuration session

### Validation Rules

- `ImplicitHostDefault` must use the same host-derived assembly set as `FromHostAssemblies()`.
- `ExplicitProviders` must scan only assemblies returned by appended providers.
- Explicit mode must not implicitly add the host-derived provider unless `FromHostAssemblies()` was appended.

## Assembly Discovery Set

Represents the aggregate assembly set passed into `FeatureDiscovery`.

### Fields

- `ProviderContributions`: ordered provider outputs collected from the selected provider list
- `DeduplicatedAssemblies`: the distinct runtime assemblies that will be scanned
- `AssemblyIdentity`: the runtime identity used to suppress duplicate scanning

### Validation Rules

- All configured provider contributions are concatenated before deduplication.
- Duplicate assemblies contributed by multiple providers appear only once in `DeduplicatedAssemblies`.
- First-seen provider order is preserved when duplicates are removed.
- The aggregate set may be empty in explicit mode if all appended providers contribute zero assemblies.

## Built-in Host Feature Assembly Provider

Represents the built-in provider that supplies the host-derived scan set.

### Fields

- `ResolutionStrategy`: the extracted helper that mirrors current default host assembly resolution
- `ConfiguredBy`: implicit default mode or explicit `FromHostAssemblies()` call

### Validation Rules

- The returned assembly set must match today’s default host-derived discovery behavior exactly.
- Combining this provider with other providers is additive.
- Repeated registration of the host provider is allowed, but final discovery remains deduplicated.

## Built-in Explicit Feature Assembly Provider

Represents the built-in provider that returns a fixed developer-supplied assembly set.

### Fields

- `ConfiguredAssemblies`: the assemblies supplied to `FromAssemblies(...)`
- `ConfiguredOrder`: the order those assemblies were supplied in the fluent call

### Validation Rules

- A `null` explicit assembly input must be rejected with a clear developer-facing error.
- An empty explicit assembly input is valid and contributes zero assemblies.
- Assemblies contributed across multiple explicit-provider registrations are all included additively before deduplication.

## Naming Decision Record

Represents the approved terminology for the new public API surface.

### Fields

- `ProviderInterfaceName`: approved interface name for the public extension point
- `BuilderMethodNames`: approved fluent names for built-in and custom provider registration
- `BuiltInProviderNames`: approved implementation names for the built-in host and explicit providers
- `RejectedAlternatives`: deprecated or rejected naming candidates with rationale

### Validation Rules

- One approved naming set must be used consistently across contracts, implementation, tests, and docs.
- Rejected names must not remain in public examples or documentation after the feature ships.
