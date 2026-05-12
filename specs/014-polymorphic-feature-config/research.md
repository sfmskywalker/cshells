# Research: Polymorphic Feature Configuration

## Decision: Extend the feature entry model with explicit declaration state

**Rationale**: `EnabledFeatures` alone cannot distinguish "not mentioned" from "explicitly disabled", which is required for code-first defaults to be overridable by higher-priority configuration. Extending the normalized feature entry/declaration model lets parsing preserve `true`, `false`, and object intent until merge resolution produces the final enabled feature set.

**Alternatives considered**:

- Keep only `EnabledFeatures`: rejected because disabled configuration cannot remove code-first defaults.
- Store disablement as magic keys in `ConfigurationData`: rejected because it leaks control state into feature option binding and management output.
- Add public `DisabledFeatures` configuration sections: rejected by the spec because it worsens DX and splits one concept across two places.

## Decision: Keep the public configuration contract as a named map

**Rationale**: Named maps merge predictably across layered configuration providers and support stable environment-variable paths. Arrays are still present in legacy code, but the new contract should prefer map syntax and avoid recommending array feature lists.

**Alternatives considered**:

- Feature arrays with string entries: attractive for compact local JSON, but brittle across layered configuration because provider merging is index-based.
- Separate `EnabledFeatures` and `DisabledFeatures`: explicit, but creates conflict rules and makes feature settings awkward.

## Decision: Interpret `true` as enable-with-defaults and reset inherited child settings

**Rationale**: A higher-priority `true` is a positive declaration that asks for defaults. If it inherited lower-priority settings, deployers would have no compact way to opt back into a feature while dropping packaged defaults.

**Alternatives considered**:

- Let configuration provider child merging leak through under `true`: rejected because it surprises users and violates the clarification.
- Make all positive declarations replace lower-priority settings: rejected because object entries should still support ordinary partial option overrides.

## Decision: Let object entries merge normally and bind directly as feature settings

**Rationale**: Object entries are the configured-options form. Preserving direct binding paths avoids a migration tax and keeps common layered option override behavior.

**Alternatives considered**:

- Introduce a `Settings` wrapper: rejected by the spec as too verbose.
- Treat an object property named `Enabled` as control metadata: rejected because it would break feature option names and make direct binding ambiguous.

## Decision: Accept native booleans and case-insensitive string `true` / `false`

**Rationale**: Environment variables and some providers surface scalar values as strings, so string booleans are needed for deployment overrides. Restricting accepted strings to `true` and `false` keeps validation precise.

**Alternatives considered**:

- Native booleans only: rejected because environment-variable enable/disable would be impractical.
- Accept `yes`, `no`, `1`, and `0`: rejected because it broadens the grammar without user value and makes invalid config easier to miss.

## Decision: Unknown disabled features are no-op declarations; unknown positive entries fail

**Rationale**: Shared deployment configuration should be able to disable optional features across application variants. Positive declarations still need validation because they attempt to activate unavailable behavior and often indicate typos or missing assemblies.

**Alternatives considered**:

- Fail every unknown feature: rejected because it harms portable deployment config.
- Ignore every unknown feature: rejected because it hides attempted activations and misspellings.

## Decision: Validate before activation and preserve existing dependency failure semantics

**Rationale**: Disabled features must be removed before dependency resolution and service configuration. If another enabled feature depends on a disabled feature, the system should behave as though that dependency is absent and surface the existing dependency validation error.

**Alternatives considered**:

- Disable dependents automatically: rejected because it would hide configuration mistakes.
- Activate disabled dependencies to satisfy dependent features: rejected because it contradicts explicit disablement.
