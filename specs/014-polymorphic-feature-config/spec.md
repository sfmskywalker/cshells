# Feature Specification: Polymorphic Feature Configuration

**Feature Branch**: `014-polymorphic-feature-config`  
**Created**: 2026-05-11  
**Status**: Draft  
**Input**: User description: "Make CShells feature configuration more pleasant and override-friendly by keeping `Features` as a named feature map while allowing each feature entry to be expressed as `true`, `false`, or an object. `true` enables a feature with default settings, `false` explicitly disables a feature even if an earlier configuration source or code-first default enabled it, and an object enables the feature while binding that object directly as feature settings. Avoid `EnabledFeatures`/`DisabledFeatures` split sections and avoid an extra `Settings` nesting layer. Preserve existing `{}` feature entries as enabled-with-defaults. Define layered precedence clearly so later configuration sources can disable or re-enable earlier feature declarations, including Docker-mounted configuration overriding application defaults."

## Clarifications

### Session 2026-05-11

- Q: Should code-first feature registrations be overridable by configuration? → A: Code-first feature registrations are overridable defaults; higher-priority configuration can disable or re-enable them.
- Q: How should higher-priority `true` interact with lower-priority feature settings? → A: `false` disables despite inherited children; `true` enables with defaults and ignores inherited children; object entries enable with normally merged object settings.
- Q: Which scalar boolean representations should feature entries accept? → A: Accept native booleans and case-insensitive string `true` / `false`; reject all other scalar values.
- Q: How should disablement behave for unknown feature names? → A: Unknown feature set to `false` is allowed as a no-op and may be surfaced in diagnostics; unknown feature set to `true` or object remains invalid.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Enable Features With Compact Values (Priority: P1)

As a CShells configuration author, I want to enable features with `true` when no feature-specific settings are needed so configuration files are easier to scan and do not require empty objects.

**Why this priority**: This is the most visible DX improvement. It removes noisy `{}` entries while preserving the named feature map that works well with layered configuration.

**Independent Test**: Can be fully tested by loading a shell configuration with feature entries set to `true`, `{}`, and configured objects, then confirming each enabled feature is present with the expected settings.

**Acceptance Scenarios**:

1. **Given** a shell configuration contains `"DefaultAuthentication": true`, **When** shell settings are loaded, **Then** the `DefaultAuthentication` feature is enabled with default settings.
2. **Given** a shell configuration contains `"DefaultAuthentication": {}`, **When** shell settings are loaded, **Then** the `DefaultAuthentication` feature remains enabled with default settings for backward compatibility.
3. **Given** a shell configuration contains `"Http": { "HttpActivityOptions": { "BasePath": "/workflows" } }`, **When** shell settings are loaded, **Then** the `Http` feature is enabled and receives the configured settings directly from the object.

---

### User Story 2 - Disable Default Features From Later Configuration (Priority: P2)

As an operator using a packaged application or Docker image, I want a mounted configuration file to disable features that were enabled by application defaults so I can tailor the runtime shell without modifying the source application.

**Why this priority**: The current presence-based model can add or override feature settings but cannot express removal. Explicit disablement is the core operational need.

**Independent Test**: Can be fully tested by loading base application configuration plus a higher-priority configuration source that sets an existing feature entry to `false`, then confirming the final shell does not activate that feature.

**Acceptance Scenarios**:

1. **Given** base configuration enables `Identity` with settings, **When** a later configuration source sets `"Identity": false`, **Then** the final shell does not enable `Identity`.
2. **Given** base configuration enables several features, **When** a later configuration source disables one feature, **Then** unrelated enabled features and their settings remain unchanged.
3. **Given** an application provides code-first feature defaults for a shell, **When** deploy-time configuration explicitly sets one of those feature names to `false`, **Then** the final shell does not activate that feature.
4. **Given** an environment variable override sets a feature entry to `"false"` using the configuration provider's string value form, **When** shell settings are loaded, **Then** the value is interpreted as an explicit feature disablement.
5. **Given** shared deployment configuration disables a feature not present in the current application, **When** shell settings are loaded, **Then** the unknown disabled feature is ignored without preventing shell activation.

---

### User Story 3 - Re-Enable Or Reconfigure Features From Later Configuration (Priority: P3)

As an operator or application maintainer, I want later configuration sources to be able to re-enable a feature by setting it to `true` or to an object so the highest-priority configuration expresses the final intent.

**Why this priority**: Disablement must be reversible across layered configuration. Without re-enable semantics, a lower-priority `false` would be too sticky for deployment-specific customization.

**Independent Test**: Can be fully tested by composing multiple configuration sources where an earlier source disables a feature and a later source enables or configures it, then confirming the later declaration wins.

**Acceptance Scenarios**:

1. **Given** an earlier configuration source sets `"Identity": false`, **When** a later source sets `"Identity": true`, **Then** the final shell enables `Identity` with default settings.
2. **Given** an earlier configuration source sets `"Identity": false`, **When** a later source sets `"Identity": { "SigningKey": "configured" }`, **Then** the final shell enables `Identity` with the provided settings.
3. **Given** an earlier configuration source provides settings for `Identity`, **When** a later source sets `"Identity": true`, **Then** the final shell enables `Identity` with default settings and does not inherit the earlier settings.
4. **Given** multiple configuration layers declare object settings for the same feature, **When** the final shell settings are resolved, **Then** object settings merge according to normal configuration precedence.

---

### User Story 4 - Keep Feature Settings Direct And Familiar (Priority: P4)

As a developer configuring feature options, I want configured feature objects to continue binding directly to feature settings so I do not need to add a wrapper such as `Settings`.

**Why this priority**: Extra nesting would make every configured feature more verbose and would make existing configuration harder to migrate.

**Independent Test**: Can be fully tested by loading existing object-based feature configuration and confirming feature options bind from the same paths as before.

**Acceptance Scenarios**:

1. **Given** an existing feature configuration object contains option properties directly under the feature name, **When** the new format is used, **Then** those option properties continue to bind without a `Settings` wrapper.
2. **Given** a feature object contains a property named `Enabled`, **When** feature settings are bound, **Then** that property is treated as feature settings rather than as the feature enablement flag.
3. **Given** documentation shows configured feature options, **When** a developer copies the example, **Then** the example uses direct option nesting under the feature name.

### Edge Cases

- A feature entry set to `false` must disable the feature even when lower-priority configuration still contains child settings for that feature.
- A later `true` declaration must re-enable a feature disabled by an earlier source with default settings and without inherited lower-priority child settings.
- A later object declaration must re-enable a feature disabled by an earlier source and merge object settings according to normal configuration precedence.
- A feature entry set to `null` is invalid because it does not clearly express enable, disable, or settings.
- A feature entry with an empty object remains valid and means enable with default settings.
- Feature names with blank or whitespace-only keys are invalid and must fail before shell activation.
- Invalid scalar values such as `"yes"`, `0`, or arbitrary strings other than case-insensitive `true` / `false` must fail with an actionable error rather than being guessed.
- Unknown feature names set to `false` are valid no-op disablements and should remain visible for diagnostics where available.
- Unknown feature names set to `true` or to an object are invalid because they represent attempted activation of an unavailable feature.
- Duplicate feature declarations across configuration sources must resolve according to source priority, not by producing duplicate feature activations.
- Disabled features must not run service configuration, endpoint mapping, dependency activation, or post-configuration hooks.
- Disabling a feature that other enabled features depend on must produce the same dependency validation behavior as if the feature were absent.
- Documentation must make clear that arrays are not the primary feature configuration format because named maps merge more predictably across configuration layers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST continue to represent per-shell features as a named map under each shell's `Features` section.
- **FR-002**: The system MUST support a feature entry value of `true` to enable the named feature with default settings.
- **FR-003**: The system MUST support a feature entry value of `false` to explicitly disable the named feature.
- **FR-004**: The system MUST support a feature entry value of an object to enable the named feature and bind that object directly as the feature's settings.
- **FR-005**: The system MUST preserve existing empty object entries such as `"FeatureName": {}` as enabled-with-defaults declarations.
- **FR-006**: The system MUST NOT require or introduce `EnabledFeatures`, `DisabledFeatures`, `Enabled`, or `Settings` wrapper sections for the primary feature configuration format.
- **FR-007**: The system MUST treat a direct feature object property named `Enabled` as ordinary feature settings, not as the feature enablement flag.
- **FR-008**: The system MUST allow a higher-priority configuration source to disable a feature enabled by a lower-priority configuration source.
- **FR-009**: The system MUST treat application code-first feature registrations as defaults that higher-priority configuration can disable or re-enable.
- **FR-010**: The system MUST allow a higher-priority configuration source to re-enable a feature disabled by a lower-priority source by setting the feature entry to `true`.
- **FR-011**: The system MUST allow a higher-priority configuration source to re-enable and configure a feature disabled by a lower-priority source by setting the feature entry to an object.
- **FR-012**: The system MUST treat a higher-priority `true` declaration as enabled-with-defaults and MUST NOT inherit lower-priority child settings for that feature.
- **FR-013**: The system MUST merge object feature settings according to normal configuration precedence when multiple configuration layers provide object values for the same enabled feature.
- **FR-014**: The system MUST ensure the final resolved feature set contains only enabled features and excludes explicitly disabled features before feature dependency resolution and activation.
- **FR-015**: The system MUST accept native boolean values and case-insensitive string values `true` and `false` as feature enablement scalars.
- **FR-016**: The system MUST validate feature entry values and reject unsupported scalar values with a clear message that identifies the feature path and expected value forms.
- **FR-017**: The system MUST reject `null` feature entry values with a clear message that instructs users to use `true`, `false`, or an object.
- **FR-018**: The system MUST allow an unknown feature entry set to `false` as a no-op disablement.
- **FR-019**: The system MUST reject an unknown feature entry set to `true` or to an object because it attempts to enable an unavailable feature.
- **FR-020**: The system SHOULD make unknown feature no-op disablements visible in diagnostics where diagnostic output is available.
- **FR-021**: The system MUST reject blank or whitespace-only feature names before shell activation.
- **FR-022**: The system MUST preserve existing feature option binding paths for object-based feature entries.
- **FR-023**: The system MUST document the three supported feature entry forms: `true`, `false`, and object.
- **FR-024**: Documentation and samples MUST show Docker or deployment override examples where mounted configuration disables a default feature with `false`.
- **FR-025**: Documentation MUST explain precedence behavior for disable and re-enable declarations across layered configuration sources and code-first defaults.
- **FR-026**: Documentation MUST avoid recommending array-based feature lists as the primary configuration format.

### Key Entities *(include if feature involves data)*

- **Feature Configuration Map**: The per-shell collection under `Features` whose keys are feature names and whose values declare enablement, disablement, or feature settings.
- **Feature Entry**: A single named item in the feature configuration map, represented as `true`, `false`, or an object.
- **Enabled Feature Declaration**: A feature entry that resolves to enabled, either because its value is `true`, `{}`, or a settings object.
- **Disabled Feature Declaration**: A feature entry whose value is `false` and which removes that feature from the final activated feature set.
- **Feature Settings Object**: A direct object value under a feature name that both enables the feature and provides bindable settings.
- **Configuration Layer**: A source of configuration values, such as application defaults, deployment configuration, mounted Docker configuration, environment variables, or code-first defaults, that contributes to the final feature state.
- **Resolved Feature Set**: The final per-shell collection of features that will participate in dependency validation, service configuration, endpoint mapping, and activation.

### Assumptions

- The named feature map remains the preferred shape because it merges more predictably than arrays across layered configuration sources.
- Feature object values continue to bind directly to configurable feature options.
- `false` is the only supported disablement syntax in configuration.
- `true` is the preferred compact enablement syntax when a feature has no settings.
- String `true` and `false` values are supported so environment variables and other string-valued configuration providers can enable and disable features.
- Existing object-based feature entries remain valid to avoid unnecessary migration churn.
- The highest-priority explicit declaration for a feature determines its final enabled or disabled state.
- A `true` declaration is intentionally a reset to feature defaults, while object declarations preserve normal layered option merging.
- Unknown feature disablements are allowed to support reusable deployment configuration across application variants.
- Code-first feature registrations participate as overridable defaults rather than authoritative declarations.
- Application authors expect deployment configuration, such as a Docker-mounted file, to be able to opt out of default features supplied by the packaged application.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of tests for `true`, `{}`, and object feature entries confirm those forms enable the named feature.
- **SC-002**: A layered configuration test confirms a higher-priority `false` disables a feature enabled by lower-priority configuration while leaving unrelated features unchanged.
- **SC-003**: A layered configuration test confirms a higher-priority `true` re-enables a feature disabled by a lower-priority source.
- **SC-004**: A layered configuration test confirms a higher-priority object re-enables a disabled feature and binds the object's settings directly.
- **SC-005**: A code-first default feature can be disabled by higher-priority deployment configuration in verified tests.
- **SC-006**: 100% of invalid value tests for `null`, blank feature names, and unsupported scalar values fail before shell activation with actionable messages.
- **SC-007**: Existing object-based feature option binding tests continue to pass without adding a `Settings` wrapper.
- **SC-008**: Documentation and samples include at least one compact `true` example, one `false` disablement example, one object settings example, and one Docker-mounted override example.
- **SC-009**: A layered configuration test confirms a higher-priority `true` declaration does not inherit lower-priority feature option values.
- **SC-010**: Environment-style string values `"true"` and `"false"` are accepted case-insensitively, while values such as `"yes"` and `"0"` are rejected in verified tests.
- **SC-011**: Unknown feature entries set to `false` do not prevent shell activation, while unknown feature entries set to `true` or object fail with actionable messages in verified tests.
