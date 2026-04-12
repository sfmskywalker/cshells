# Phase 0 Research: Fluent Builder Naming Matrix

## Decision: Treat feature 004 as implementation-backed protection of an already approved API surface

**Rationale**: The current repository already ships the approved assembly-discovery entry points in `src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`: `FromAssemblies(...)`, `FromHostAssemblies()`, and three overload shapes under `WithAssemblyProvider(...)`. The new planning scope therefore should not reopen naming design or force renames. Instead, it should define the smallest execution scope that preserves those shipped names, protects them against regression, and keeps guidance aligned with the same decision.

**Alternatives considered**:

- Reframe 004 as design-only again: rejected because the updated spec now requires repository guardrails and guidance alignment, not just decision recording.
- Reopen the public naming surface and search for alternative verbs: rejected because the approved matrix and names remain fixed for this feature.

## Decision: Reuse `specs/003-fluent-assembly-selection` as prior art, not as the implementation backlog for 004

**Rationale**: Feature 003 remains the original decision context for the assembly-discovery API and already explains why `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` were approved. Feature 004 now builds on that baseline by defining how the repository keeps those decisions intact through tests and guidance audits. This preserves one naming trajectory while giving downstream task generation a real implementation scope.

**Alternatives considered**:

- Replace 003 with 004 as the sole source of truth: rejected because it would blur prior-art rationale with current enforcement work.
- Duplicate every 003 artifact in 004: rejected because 004 only needs the parts of 003 that inform naming preservation and verification.

## Decision: Keep the approved verb-family matrix fixed as `From*` for source selection and `With*` for provider attachment

**Rationale**: The existing builder surface cleanly maps to the approved responsibilities. `FromAssemblies(...)` and `FromHostAssemblies()` answer where discovery gets assemblies from, so they stay in the source-selection family. `WithAssemblyProvider(...)` attaches an extensibility component, so it stays in the provider-attachment family. Keeping this matrix fixed prevents future contributions from relitigating already approved naming intent.

**Alternatives considered**:

- Allow both `From*` and `With*` for source-selection methods: rejected because it weakens the distinction the repository already relies on.
- Allow `Add*` aliases for convenience: rejected because those names communicate raw mutation rather than the approved builder vocabulary.

## Decision: Implement naming guardrails as focused xUnit public-surface tests in the existing test project

**Rationale**: The constitution requires xUnit for new verification, and the repository already has focused unit coverage around assembly-source behavior in `tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs`. A small reflection-based or equivalent public-surface test in the same area can assert that the approved method groups remain present, that `WithAssemblyProvider(...)` may have multiple overloads under the same approved name, and that rejected replacement names such as `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, and `AddHostAssemblies` are not introduced as competing public entry points.

**Alternatives considered**:

- Rely on manual review only: rejected because the spec now requires durable regression protection.
- Add a Roslyn analyzer or third-party public API validation package: rejected because it is heavier than needed, adds tooling complexity, and is not required to protect this narrow surface.
- Use integration tests alone: rejected because behavior tests confirm discovery semantics but do not directly lock the public naming surface.

## Decision: Treat `WithAssemblyProvider(...)` as one approved naming family with multiple valid overload shapes

**Rationale**: The current implementation exposes the approved provider-attachment entry point through generic, instance, and factory overloads. That variation is consistent with the spec and should not be treated as naming drift. The guardrails should verify that all supported attachment shapes continue to use the approved `WithAssemblyProvider(...)` name rather than constraining the implementation to a single overload.

**Alternatives considered**:

- Require exactly one `WithAssemblyProvider(...)` overload: rejected because it would incorrectly classify legitimate API convenience overloads as violations.
- Ignore overload shape entirely: rejected because the spec explicitly wants protection that still acknowledges multiple supported attachment forms.

## Decision: Limit guidance alignment to assets that already describe assembly discovery in code comments, docs, wiki, or samples

**Rationale**: Repository search shows the approved naming surface is already echoed in a small, identifiable set of assets: `README.md`, `docs/getting-started.md`, `docs/multiple-shell-providers.md`, `src/CShells.AspNetCore/README.md`, `src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs`, `src/CShells.AspNetCore/Extensions/ShellExtensions.cs`, `wiki/Getting-Started.md`, and `samples/CShells.Workbench/Program.cs`. That makes the documentation scope concrete and minimal: verify those assets keep using the approved names and the `From*` versus `With*` rationale, and only edit files that actually drift.

**Alternatives considered**:

- Sweep all markdown and sample files in the repo: rejected because it expands beyond the assembly-discovery builder surface.
- Skip guidance review because the API already conforms: rejected because the spec explicitly includes samples, docs, and comments in scope.

## Decision: Do not plan unrelated runtime changes, public aliases, or non-naming refactors under feature 004

**Rationale**: The current repository already appears to conform to the approved naming matrix. The value in feature 004 comes from preserving that surface, not from manufacturing change. The implementation plan should therefore stay focused on verification and targeted guidance cleanup, leaving unrelated runtime refactors and broader builder vocabulary work to separate features.

**Alternatives considered**:

- Add new convenience aliases while preserving the approved names: rejected because duplicate public names for the same responsibility still create naming drift.
- Bundle broader fluent API cleanup into 004: rejected because the spec explicitly says to avoid unrelated renames.

