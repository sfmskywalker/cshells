# Naming Decision Record: Fluent Assembly Source Selection

## Fluent Builder Naming Matrix

| Builder action | Verb family | Use when | Approved examples | Why |
|---|---|---|---|---|
| Source selection | `From*` | The method describes where feature discovery gets assemblies from. | `FromAssemblies(...)`, `FromHostAssemblies()` | `From*` reads as a discovery-source selector and keeps additive source configuration mentally separate from provider attachment. |
| Provider attachment | `With*` | The method attaches a provider object, factory, or type to extend discovery behavior. | `WithAssemblyProvider(...)` | `With*` matches existing CShells builder vocabulary for attaching extensibility components. |

## Approved Naming Set

| Area | Approved Name | Why it was chosen |
|---|---|---|
| Public interface | `IFeatureAssemblyProvider` | Clearly states that the contract supplies assemblies specifically for feature discovery. |
| Explicit assemblies fluent method | `FromAssemblies(...)` | Reads as an additive source-selection operation rather than a replacement-oriented setter. |
| Host-derived fluent method | `FromHostAssemblies()` | Makes the built-in default-equivalent source explicit and discoverable. |
| Custom provider fluent method | `WithAssemblyProvider(...)` | Matches existing CShells builder vocabulary for appending provider-like extensibility components. |
| Built-in host provider type | `HostFeatureAssemblyProvider` | Connects the implementation to host-derived discovery without implying replacement semantics. |
| Built-in explicit provider type | `ExplicitFeatureAssemblyProvider` | Mirrors the spec’s “explicit assemblies” concept directly. |
| Internal builder registration field | `_featureAssemblyProviderRegistrations` | Describes stored contents precisely, reflects the registration-based design, and follows the constitution's private-field naming convention. |
| Internal aggregation helper | `FeatureAssemblyResolver` | Describes a helper that materializes the effective assembly set from the configured providers. |

## Candidate Evaluation

| Candidate | Outcome | Verb family fit | Rationale |
|---|---|---|---|
| `FromAssemblies(...)` | Approved | Correct | Selects an explicit assembly discovery source, so it belongs in the `From*` family. |
| `FromHostAssemblies()` | Approved | Correct | Selects the host-derived discovery source explicitly while matching default-behavior semantics. |
| `WithAssemblies(...)` | Rejected | Incorrect | Uses the provider-attachment family for a source-selection action, which blurs the naming matrix. |
| `WithHostAssemblies()` | Rejected | Incorrect | Has the same verb-family mismatch as `WithAssemblies(...)` and obscures that host assemblies are a discovery source. |
| `AddAssemblies(...)` | Rejected | Weak | Communicates raw list mutation more than discovery-source selection, so it is less precise than `FromAssemblies(...)`. |
| `AddHostAssemblies()` | Rejected | Weak | Suggests direct collection mutation rather than choosing the built-in host-derived discovery source. |
| `WithAssemblyProvider(...)` | Approved | Correct | Attaches a provider abstraction, so it naturally belongs in the `With*` family. |

## Rejected Alternatives

| Rejected Name | Rejection Reason |
|---|---|
| `UseAssemblies(...)` | `Use*` commonly suggests replacement semantics, which conflicts with additive composition. |
| `UseHostAssemblies()` | Same replacement-semantics concern as `UseAssemblies(...)`. |
| `WithAssemblies(...)` | Too ambiguous between mutation of a single property and appending a new discovery source. |
| `WithHostAssemblies()` | Uses the provider-attachment family for a source-selection action and hides the source-selection intent. |
| `AddAssemblies(...)` | Reads like direct list mutation rather than provider-backed source configuration. |
| `AddHostAssemblies()` | Reads like direct list mutation rather than explicit selection of the built-in host-derived source. |
| `IAssemblySourceProvider` | Redundant and less natural than `IFeatureAssemblyProvider`. |
| `IDiscoveryAssemblyProvider` | Accurate but less user-friendly and less aligned with the existing `CShells.Features` vocabulary. |
| `DefaultAssemblyProvider` | Too vague; it hides the crucial host-derived behavior. |
| `ConfiguredAssemblyProvider` | Too generic; it does not distinguish explicit developer-supplied assemblies from other configuration styles. |

## Naming Usage Rules

- Public docs and examples should use the approved fluent method names only.
- Public docs and examples should apply the `From*` versus `With*` matrix consistently when describing future builder verbs around assembly discovery.
- Implementation, tests, and docs should not mix “source” and “provider” terminology inconsistently for the same concept.
- The host-derived path should be described as “host assemblies” rather than “default assemblies” whenever the behavior must be explicit.
- The old assembly-argument API names remain only in migration context, not as active recommendations.
- Already approved names should be preserved unless a stronger documented alternative clearly improves both clarity and matrix consistency.
