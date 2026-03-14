# Feature Specification: Feature Object Map

**Feature Branch**: `002-feature-object-map`  
**Created**: 2026-03-14  
**Status**: Draft  
**Input**: User description: "Support object-map syntax for shell Features configuration alongside the current array syntax"

## Clarifications

### Session 2026-03-14

- Q: In object-map syntax, how should an inner `Name` property be interpreted? → A: Treat `Name` like any other setting; only the map key identifies the feature.
- Q: How should object-map feature order be handled before dependency resolution? → A: Preserve object-map entry order exactly as declared in configuration.
- Q: Which configuration entry points should accept object-map syntax? → A: Support object-map syntax for configuration providers and direct JSON deserialization of shell config models.
- Q: Which syntax should be preferred when serializing shell config models back to JSON? → A: Prefer serializing object-map syntax whenever possible.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure Features Without Repeating Names (Priority: P1)

As a shell configuration author, I want to declare enabled features as a named object map so I can configure each feature in one place without repeating the feature name inside each object.

**Why this priority**: This is the new capability being requested. It reduces configuration duplication and makes feature-specific settings easier to scan and maintain.

**Independent Test**: Can be fully tested by loading a shell definition whose `Features` value is an object map and confirming the shell enables the expected features and applies the configured settings to each feature.

**Acceptance Scenarios**:

1. **Given** a shell configuration that defines `Features` as an object map with empty objects for some features, **When** the shell settings are loaded, **Then** each object key is treated as an enabled feature.
2. **Given** a shell configuration that defines `Features` as an object map with one feature containing settings, **When** the shell settings are loaded, **Then** the feature is enabled and its settings are available through the same shell configuration path used by existing configured features.

---

### User Story 2 - Preserve Existing Array-Based Configurations (Priority: P2)

As an existing CShells user, I want current array-based feature definitions to keep working so I can adopt the new syntax only when I choose to.

**Why this priority**: Backward compatibility protects existing applications and samples from forced migration.

**Independent Test**: Can be fully tested by loading existing shell configurations that use string entries and `{ "Name": "..." }` objects and confirming their enabled features and settings remain unchanged.

**Acceptance Scenarios**:

1. **Given** a shell configuration that defines `Features` as an array of strings and named objects, **When** the shell settings are loaded, **Then** the resulting enabled features and feature settings match current behavior.
2. **Given** two equivalent shell definitions, one using the array syntax and one using the object-map syntax, **When** both are loaded, **Then** they produce the same feature enablement and the same feature-specific configuration values.
3. **Given** a shell config model deserialized directly from JSON using object-map syntax, **When** that model is loaded through the same shell settings pipeline, **Then** it produces the same feature enablement and feature settings as the equivalent configuration-provider-based input.
4. **Given** a shell config model containing enabled features with and without settings, **When** that model is serialized back to JSON, **Then** the `Features` value is emitted using object-map syntax with empty objects for features that have no explicit settings.

---

### User Story 3 - Receive Clear Feedback For Invalid Definitions (Priority: P3)

As a shell configuration author, I want invalid feature configuration shapes to fail clearly so I can correct mistakes before a shell is activated.

**Why this priority**: Supporting multiple valid shapes increases the chance of malformed input; clear validation prevents ambiguous startup behavior.

**Independent Test**: Can be fully tested by loading invalid feature definitions and verifying the system rejects them with an actionable error that identifies the shell and invalid feature entry.

**Acceptance Scenarios**:

1. **Given** a shell configuration where a feature map entry is not an object, **When** the shell settings are loaded, **Then** loading fails with an error that identifies the invalid feature entry.
2. **Given** a shell configuration source that produces an ambiguous `Features` definition for the same shell, **When** the shell settings are loaded, **Then** loading fails instead of silently choosing one interpretation.
3. **Given** a shell config model with an invalid object-map feature entry loaded through direct JSON deserialization, **When** the model is converted into runtime shell settings, **Then** loading fails with an error that identifies the shell name and the invalid feature entry.
4. **Given** a shell configuration loaded through `IConfiguration` that configures the same feature more than once, **When** the shell settings are loaded, **Then** loading fails with an error that identifies the shell and duplicate feature name.

### Edge Cases

- A shell defines `Features` as an empty object map and should load with no enabled features.
- A feature map contains nested settings objects and arrays, which must remain available to the feature as structured configuration values.
- A feature map contains a null, scalar, or array value for a feature entry and must be rejected as invalid input.
- A feature map entry may contain a setting named `Name`; the feature identity remains the map key and the inner `Name` value remains available as feature configuration.
- Object-map entries preserve declaration order before dependency resolution is applied.
- Features serialized back to JSON use empty objects for enabled features that have no explicit settings.
- Equivalent feature definitions may be supplied in either syntax, but a single `Features` node is treated as one shape only: array or object map.
- Configuration providers that merge values into the same `Features` node in a way that creates an ambiguous shape must be rejected.
- A shell definition that configures the same feature more than once must be rejected rather than preserved through serialization fallback.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST continue to accept the existing array-based `Features` definition for shell configuration without requiring changes to current configurations.
- **FR-001a**: The system MUST accept supported `Features` syntax consistently across configuration-provider loading and direct JSON deserialization of shell configuration models.
- **FR-002**: The system MUST accept a `Features` object-map definition where each property name is treated as the feature name for that shell.
- **FR-003**: The system MUST treat an empty object-map value for a feature as an enabled feature with no feature-specific settings.
- **FR-004**: The system MUST treat every property inside an object-map feature value as configuration data for that feature.
- **FR-004a**: In object-map syntax, the system MUST determine feature identity exclusively from the map key and MUST NOT assign special meaning to an inner `Name` property.
- **FR-005**: The system MUST preserve nested feature settings from object-map values so they remain available through the same feature configuration access pattern used by existing configured features.
- **FR-005a**: The system MUST preserve object-map declaration order as the configured feature order before any dependency ordering is applied.
- **FR-006**: The system MUST produce the same enabled feature names and feature-specific configuration values for semantically equivalent array-based and object-map definitions.
- **FR-006a**: When serializing shell configuration models to JSON, the system MUST prefer object-map syntax for the `Features` value whenever the configured features can be represented in that syntax.
- **FR-006b**: When serializing an enabled feature with no explicit settings in object-map syntax, the system MUST emit that feature with an empty object value.
- **FR-007**: The system MUST continue to support the current array object form where a feature is declared as an object containing a `Name` property and additional settings.
- **FR-008**: The system MUST reject object-map feature entries whose value is not an object.
- **FR-009**: The system MUST report invalid feature definitions with an actionable error that identifies the affected shell and feature entry.
- **FR-010**: The system MUST reject ambiguous `Features` definitions for the same shell rather than silently merging array and object-map interpretations.
- **FR-011**: The system MUST reject duplicate configured feature names within a single shell definition, regardless of whether the input uses array syntax or object-map syntax.

### Key Entities *(include if feature involves data)*

- **Shell Definition**: A single configured shell containing the shell name, enabled features, and shell-level configuration.
- **Feature Definition**: A single enabled feature within a shell, identified by feature name and optionally carrying feature-specific settings.
- **Feature Settings Payload**: The set of simple or nested values associated with one configured feature, regardless of whether the feature was defined through array syntax or object-map syntax.

### Assumptions

- A single `Features` node is represented as either an array or an object map in any one JSON document.
- Empty object values such as `"Core": {}` are valid and indicate a feature with no explicit settings.
- In object-map syntax, feature identity comes only from the property name; inner properties, including `Name`, are treated as feature settings.
- Rejecting ambiguous merged shapes is preferable to silently selecting one interpretation because ambiguous startup behavior is harder to diagnose.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of existing acceptance tests for array-based feature configuration continue to pass without modification to configuration inputs.
- **SC-002**: A shell configuration author can express a shell with at least three enabled features, including one configured feature, using object-map syntax without repeating any feature name inside the feature values.
- **SC-003**: Equivalent array-based and object-map shell definitions produce identical enabled feature lists and identical feature setting values in 100% of comparison tests.
- **SC-003a**: Equivalent object-map inputs loaded through configuration providers and direct JSON deserialization produce identical enabled feature lists and identical feature setting values in 100% of comparison tests.
- **SC-003b**: 100% of serialization tests for shell configuration models emit the `Features` value in object-map syntax, including empty object values for features without explicit settings.
- **SC-004**: 100% of invalid object-map definitions covered by acceptance tests fail before shell activation with an error message that names the affected shell and invalid feature entry.
