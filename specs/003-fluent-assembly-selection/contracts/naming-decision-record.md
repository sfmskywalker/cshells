# Naming Decision Record: Fluent Assembly Source Selection

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

## Rejected Alternatives

| Rejected Name | Rejection Reason |
|---|---|
| `UseAssemblies(...)` | `Use*` commonly suggests replacement semantics, which conflicts with additive composition. |
| `UseHostAssemblies()` | Same replacement-semantics concern as `UseAssemblies(...)`. |
| `WithAssemblies(...)` | Too ambiguous between mutation of a single property and appending a new discovery source. |
| `AddAssemblies(...)` | Reads like direct list mutation rather than provider-backed source configuration. |
| `IAssemblySourceProvider` | Redundant and less natural than `IFeatureAssemblyProvider`. |
| `IDiscoveryAssemblyProvider` | Accurate but less user-friendly and less aligned with the existing `CShells.Features` vocabulary. |
| `DefaultAssemblyProvider` | Too vague; it hides the crucial host-derived behavior. |
| `ConfiguredAssemblyProvider` | Too generic; it does not distinguish explicit developer-supplied assemblies from other configuration styles. |

## Naming Usage Rules

- Public docs and examples should use the approved fluent method names only.
- Implementation, tests, and docs should not mix “source” and “provider” terminology inconsistently for the same concept.
- The host-derived path should be described as “host assemblies” rather than “default assemblies” whenever the behavior must be explicit.
- The old assembly-argument API names remain only in migration context, not as active recommendations.

