# Data Model: Polymorphic Feature Configuration

## Feature Configuration Map

Per-shell configuration section at `CShells:Shells:{ShellName}:Features`.

Fields:

- `FeatureName`: map key. Required, non-blank after trimming.
- `FeatureValue`: one of native boolean, case-insensitive string boolean, or object.

Validation:

- Blank feature names are invalid.
- `null` values are invalid.
- Unsupported scalar values are invalid.
- Arrays are not part of the primary contract.

## Feature Declaration

Normalized representation of one configured feature entry.

Fields:

- `Name`: feature name from the map key or code-first API.
- `State`: enabled or disabled.
- `Settings`: direct feature option values when the declaration is an object.
- `ResetsSettings`: true when a higher-priority `true` declaration should drop lower-priority settings.
- `Source`: optional diagnostic description such as shell name or configuration path.

Validation:

- Enabled declarations require the feature to exist in the runtime catalog.
- Disabled declarations may name unknown features and become no-op disablements.
- Object declarations are enabled declarations and preserve direct settings.

State transitions:

- Absent → Enabled by `true`, `{}`, object, or code-first registration.
- Enabled → Disabled by higher-priority `false`.
- Disabled → Enabled with defaults by higher-priority `true`.
- Disabled → Enabled with merged settings by higher-priority object.

## Shell Settings

Runtime composition state for a shell.

Fields:

- `Id`: shell identifier.
- `EnabledFeatures`: final enabled feature names after declaration merge.
- `DisabledFeatures`: explicit disabled feature declarations that must remove lower-priority defaults during merge.
- `ConfigurationData`: flattened shell and feature option settings.
- `FeatureConfigurators`: code-first feature instance configurators.

Validation:

- `EnabledFeatures` should contain only features selected for dependency resolution and activation.
- Explicit disabled features must remove matching enabled feature names before dependency resolution.
- Configuration data for a feature reset by `true` must not keep lower-priority feature option keys.

Relationships:

- A shell has one feature configuration map.
- A feature configuration map produces zero or more feature declarations.
- Feature declarations resolve into `ShellSettings.EnabledFeatures`, `ShellSettings.DisabledFeatures`, and feature-prefixed `ConfigurationData`.

## Configuration Layer

Ordered source contributing shell feature declarations.

Examples:

- Code-first shell defaults.
- Application settings.
- Mounted deployment JSON.
- Environment variables.

Merge rules:

- Later layers have higher priority.
- A higher-priority `false` disables lower-priority enabled declarations and ignores lower-priority child settings.
- A higher-priority `true` enables with defaults and removes lower-priority child settings for that feature.
- Higher-priority object declarations enable the feature and merge object settings through normal configuration precedence.

## Resolved Feature Set

Final set consumed by dependency resolution and activation.

Fields:

- `Enabled`: catalog-known feature names ordered by dependency resolver.
- `MissingPositive`: unknown features requested by `true` or object values; these are recorded for diagnostics and skipped during activation when absent from the runtime feature catalog.
- `IgnoredDisabled`: unknown features requested by `false`.

Validation:

- `MissingPositive` does not fail activation; it produces actionable warning/status diagnostics and the shell activates with available features.
- `IgnoredDisabled` does not fail shell activation.
- Disabled catalog-known features do not run configuration binding, service registration, endpoint mapping, or post-configuration hooks.
