# Research: Pattern-Based Shared Assemblies

## Decision: Use one root `CShells:SharedAssemblies` configuration collection

**Rationale**: Exact names and prefix wildcard patterns are the same conceptual user input: a host-wide selector over assembly simple names. A single collection makes configuration compact, keeps examples easy to read, and matches the clarified spec: entries without `*` are exact names; entries ending in `*` are prefix patterns.

**Alternatives considered**:

- Separate `SharedAssemblyNames` and `SharedAssemblyPatterns`: clearer at the type level, but more verbose and creates duplicate validation/deduplication paths.
- Per-shell selector configuration: rejected by clarification because shared assembly selection affects host-level discovery/isolation before shell activation.
- Pattern-only collection layered on existing explicit assembly APIs: rejected because it would split equivalent string selectors across multiple concepts.

## Decision: Restrict wildcard grammar to final-character `*`

**Rationale**: Prefix-only matching supports framework-family cases like `Elsa.*` while avoiding broad suffix or contains patterns that can accidentally share implementation assemblies. It also allows a small parser with deterministic validation and no regular expression dependency.

**Alternatives considered**:

- Glob-style `*` anywhere: too broad for an isolation boundary and more expensive to explain/test.
- Dot-only `Prefix.*`: safer, but would unnecessarily reject valid simple-name families like `Elsa*` if a host truly needs them.
- Regex patterns: too powerful for the initial feature and adds unnecessary complexity.

## Decision: Match and deduplicate by assembly simple name using `StringComparer.OrdinalIgnoreCase`

**Rationale**: The existing host assembly resolver already uses case-insensitive simple-name comparison for `AssemblyName` de-duplication. Reusing ordinal case-insensitive semantics keeps behavior consistent and independent of culture.

**Alternatives considered**:

- Current-culture case-insensitive comparison: rejected because assembly identity comparisons should not vary by UI culture.
- Case-sensitive matching: rejected by the spec and common assembly-name expectations.

## Decision: Compile/validate selectors once, then apply them during host assembly filtering

**Rationale**: The candidate assembly set is already enumerated by `FeatureAssemblyResolver.ResolveHostAssemblies`. Parsing each selector once gives clear startup/configuration failures and keeps filtering linear over `(assembly names * selectors)`.

**Alternatives considered**:

- Parse pattern strings for each assembly: simpler initially, but repeats validation work and delays invalid selector errors.
- Introduce a new discovery pipeline abstraction: rejected as unnecessary because the existing resolver already supports a filter hook.

## Decision: Keep predicate selectors code-first only

**Rationale**: Predicate selectors require executable code and source attribution. They fit the builder API, not configuration providers. Configuration remains declarative and string-pattern based.

**Alternatives considered**:

- Expression strings in configuration: rejected because it creates parsing/security complexity and a new language surface.
- Type-name predicates loaded from configuration: rejected because it overlaps with custom `IFeatureAssemblyProvider` and increases activation complexity.

## Decision: Surface diagnostics through selector source metadata

**Rationale**: Troubleshooting requires knowing whether a match came from root configuration, a builder registration, or a predicate. Store a source description alongside each selector and attach it to match diagnostics and exception messages.

**Alternatives considered**:

- Log only matched assembly names: insufficient for stale/misspelled patterns and broad wildcard troubleshooting.
- Throw without configuration path/source: violates explicit error handling expectations and makes deployment config hard to fix.
