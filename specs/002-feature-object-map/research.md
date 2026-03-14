# Phase 0 Research: Feature Object Map

## Decision: Support object-map syntax at the feature-collection boundary, not at the individual feature-entry boundary

**Rationale**: The current model already separates collection handling (`FeatureEntryListJsonConverter`, `ParseFeaturesFromConfiguration`) from individual entry handling (`FeatureEntryJsonConverter`). Object-map syntax changes how a collection of features is represented because the feature name comes from the property key rather than an item payload. Extending the collection-level parser and converter keeps existing array-item semantics intact and avoids overloading the single-entry converter with two incompatible identity models.

**Alternatives considered**:

- Teach `FeatureEntryJsonConverter` to infer names from surrounding context: rejected because an individual JSON object has no access to its containing property name.
- Introduce a second `ShellConfig` property for map syntax: rejected because it complicates the public model and forces callers to reason about mutually exclusive properties.
- Normalize everything through a new intermediate abstraction: rejected because the existing `List<FeatureEntry>` model already expresses the necessary runtime shape.

## Decision: In object-map syntax, the property key is the only feature identifier and all inner properties, including `Name`, are settings

**Rationale**: The user-requested goal is to avoid repeating feature names. Treating the object-map key as canonical keeps the format unambiguous and lets inner properties map directly to feature configuration without reserved-word exceptions.

**Alternatives considered**:

- Reserve `Name` inside map entries: rejected because it creates a surprising special case in a syntax meant to reduce repetition.
- Require inner `Name` to match the key: rejected because it reintroduces duplication without adding value.
- Let inner `Name` override the key: rejected because it makes the syntax ambiguous and harder to validate.

## Decision: Preserve declaration order for object-map entries before dependency resolution

**Rationale**: The existing array form preserves author order before the dependency resolver reorders features as needed. Maintaining the same behavior for object-map syntax prevents a new and hard-to-see ordering difference between the two accepted input forms.

**Alternatives considered**:

- Treat object maps as unordered: rejected because it would make equivalent array and object-map definitions behave differently.
- Sort object-map keys alphabetically: rejected because it would surprise authors and diverge from the array form.

## Decision: Support object-map syntax consistently for configuration-provider loading and direct JSON deserialization

**Rationale**: CShells already exposes both entry points. `ConfigurationShellSettingsProvider` and `ShellSettingsFactory.CreateFromConfiguration` handle `IConfiguration`, while `ShellConfig` plus `FeatureEntryListJsonConverter` and `FluentStorageShellSettingsProvider` handle direct JSON model deserialization. Supporting only one path would make the new syntax partial and confusing.

**Alternatives considered**:

- Support configuration providers only: rejected because FluentStorage and any direct `ShellConfig` JSON usage would lag behind the documented behavior.
- Defer direct JSON support to a later feature: rejected because the relevant conversion seam already exists and is part of the same public configuration surface.

## Decision: Reject ambiguous mixed-shape `Features` inputs explicitly

**Rationale**: JSON itself allows only one type at a key, but layered configuration providers can materialize a section that contains both numeric children from an array-like source and named children from an object-like source. Silent precedence would make startup behavior non-obvious. Explicit rejection aligns with the constitution’s error-handling rules and the clarified spec.

**Alternatives considered**:

- Prefer array interpretation when numeric children are present: rejected because named entries would be silently dropped.
- Prefer object-map interpretation when named children are present: rejected because array ordering and entries would be silently dropped.
- Merge both shapes into one list: rejected because the spec explicitly forbids silently merging array and object-map interpretations.

## Decision: Prefer object-map syntax when serializing shell config models and reject duplicate configured feature names

**Rationale**: The clarified spec chooses object-map syntax as the preferred JSON output. Emitting empty objects for features with no explicit settings makes the output consistent and fully map-shaped. Duplicate configured feature names are treated as invalid input rather than a case that requires serialization fallback, which keeps the contract simpler and avoids silent data collapse.

**Alternatives considered**:

- Always serialize using the legacy array syntax: rejected because it ignores the clarified preference for object-map output.
- Preserve whichever syntax was originally used: rejected because the current model does not track original input shape and adding that state would be unnecessary complexity.
- Allow duplicate feature names and fall back to array output: rejected because duplicate configured features are now explicitly invalid input.

## Decision: Cover the feature with focused unit and integration tests plus documentation updates

**Rationale**: The parsing/serialization seams are deterministic and well-suited for unit tests, while `IConfiguration` section-shape behavior needs integration coverage. Public-facing docs and sample config should reflect the newly supported syntax so users can discover it without inspecting tests or source.

**Alternatives considered**:

- Unit tests only: rejected because `IConfiguration` section shape and ordering behavior would not be verified end to end.
- Implementation changes without docs: rejected because this feature changes supported public configuration syntax.