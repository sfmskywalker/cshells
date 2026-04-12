# Phase 0 Research: Fluent Assembly Source Selection

## Decision: Introduce a public `IFeatureAssemblyProvider` contract in `CShells.Abstractions/Features`

**Rationale**: The clarified spec makes custom assembly providers a supported public extension point, so the abstraction must live in an abstractions project per the constitution. Placing it in `CShells.Features` keeps the contract aligned with feature discovery rather than shell-settings loading or ASP.NET Core hosting. Third-party libraries can then implement the interface without referencing `CShells` implementation assemblies.

**Alternatives considered**:

- Keep the provider abstraction internal to `CShells`: rejected because the spec explicitly requires a public custom-provider entry point.
- Place the interface in `CShells.Configuration`: rejected because the contract supplies feature-discovery assemblies, not shell settings.
- Add the contract to `CShells.AspNetCore.Abstractions`: rejected because the feature applies to both core and ASP.NET Core registration paths.

## Decision: Use a builder-managed ordered provider list to represent explicit assembly-source mode

**Rationale**: The spec requires every assembly-source call to append another provider and forbids replacement semantics. Using an ordered list on `CShellsBuilder` matches the existing provider-registration pattern already used for shell settings providers. It also gives a simple rule for explicit mode: if the builder has appended any feature-assembly provider registrations, discovery uses only those configured providers; if none were appended, CShells falls back to the built-in host-derived provider.

This approach also resolves the empty-input edge case cleanly. An empty explicit-assemblies call still appends a provider instance, so discovery enters explicit mode without re-enabling implicit host scanning.

**Alternatives considered**:

- Track a separate `bool useExplicitAssemblySources`: rejected because list presence already captures the required mode transition with less state.
- Store a mutable set instead of a list: rejected because the spec requires provider instances to be retained in call order.
- Replace the previous source on each fluent call: rejected because the spec explicitly requires additive composition.

## Decision: Reuse the current host-derived assembly resolution algorithm through a dedicated internal helper

**Rationale**: `FromHostAssemblies()` must return exactly the same assembly set that CShells currently scans when no assemblies are specified. The safest way to preserve that equivalence is to extract the existing `ResolveAssembliesToScan()` logic behind an internal helper or built-in provider implementation and have both the implicit default path and the explicit `FromHostAssemblies()` path call the same code.

**Alternatives considered**:

- Reimplement host-derived scanning separately inside a new provider: rejected because the two paths could drift over time.
- Keep the logic inline only in `ServiceCollectionExtensions`: rejected because `FromHostAssemblies()` must reuse it outside the no-configuration fallback path.
- Change the default host-derived scan set while introducing the provider abstraction: rejected because the spec requires exact behavioral equivalence.

## Decision: Aggregate provider outputs additively and deduplicate assemblies before feature discovery while preserving first-seen order

**Rationale**: The spec requires additive composition across built-in and custom providers, along with deduplicated discovery results. The provider list should therefore be evaluated in registration order, flattened into one sequence, and deduplicated before passing assemblies into `FeatureDiscovery`. Preserving first-seen order keeps behavior stable for diagnostics and testing while ensuring duplicate providers or duplicate assemblies do not trigger extra discovery work.

**Alternatives considered**:

- Skip deduplication and rely on downstream feature-name deduplication: rejected because it would do unnecessary reflection work and make explicit duplicate-source scenarios noisier.
- Deduplicate providers instead of assemblies: rejected because different providers may legitimately contribute overlapping assemblies.
- Sort assemblies alphabetically before discovery: rejected because it obscures registration-order behavior without any product requirement.

## Decision: Finalize the fluent naming set as `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`

**Rationale**: The spec calls out a naming-focused work item and emphasizes mentally consistent terminology. `FromAssemblies(...)` communicates “append an explicit assembly contribution,” `FromHostAssemblies()` communicates “append the built-in host-derived contribution,” and `WithAssemblyProvider(...)` matches existing builder vocabulary for appending extensibility components. The interface name `IFeatureAssemblyProvider` ties the abstraction to feature discovery and avoids confusion with shell-settings providers.

**Alternatives considered**:

- `UseAssemblies(...)` / `UseHostAssemblies()`: rejected because `Use*` often implies replacement semantics, which conflicts with additive composition.
- `WithAssemblies(...)`: rejected because it reads more like an in-place property setter than a source-selection operation.
- `IAssemblySourceProvider`: rejected because the double abstraction term is more awkward and less discoverable than `IFeatureAssemblyProvider`.

## Decision: Remove the legacy non-fluent assembly-argument APIs instead of preserving compatibility shims

**Rationale**: The spec explicitly allows breaking changes and prohibits keeping the previous non-fluent assembly-argument approach. Removing those overloads avoids a split mental model and keeps the public surface aligned with the new provider-based design. Migration guidance can show the new fluent equivalents directly in `README.md`, `docs/`, and ASP.NET Core examples.

**Alternatives considered**:

- Keep obsolete assembly-argument overloads temporarily: rejected because the spec forbids legacy overload support.
- Support both fluent and non-fluent forms indefinitely: rejected because it would preserve ambiguity about the preferred assembly-selection model.
- Hide the old overloads behind forwarding methods: rejected because the resulting public API would still conflict with the clarified spec.

## Decision: Validate the feature with focused unit, integration, and documentation coverage

**Rationale**: The most important risks are builder composition, default-vs-explicit host semantics, and custom-provider extensibility. These are best covered with unit tests around provider-list behavior plus integration tests that build actual service collections and verify discovery outcomes. Because the old assembly-argument API is being removed, documentation updates are part of the functional change rather than optional cleanup.

**Alternatives considered**:

- Documentation-only validation: rejected because additive composition and explicit-mode behavior are easy to regress without tests.
- Unit tests only: rejected because the feature spans builder wiring, host registration, and ASP.NET Core entry points.
- Leave examples unchanged until implementation is complete: rejected because the new public API surface must be unambiguous before the next Speckit step.

