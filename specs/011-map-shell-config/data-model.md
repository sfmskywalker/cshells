# Data Model: Map-Based Shell Configuration

## Shell Configuration Map

Represents the value under `CShells:Shells`.

### Fields

- `Entries`: ordered configuration children keyed by shell name.

### Validation Rules

- Must be an object/map shape, not an array shape.
- Immediate child keys must be non-empty, non-whitespace shell names.
- Numeric immediate child keys are invalid because they indicate unsupported array syntax.
- Keys are matched according to the host configuration system's normal key comparison behavior.

### Relationships

- Contains one `Shell Definition` per shell key.
- Provides the identity for each runtime `Shell Name`.

## Shell Definition

Represents the configuration payload for one shell.

### Fields

- `Configuration`: optional shell-specific configuration values, flattened into colon-separated runtime configuration data.
- `Features`: optional feature configuration map using the existing feature-entry contract.

### Validation Rules

- Must not rely on an inner shell-level `Name` property for identity.
- If an inner shell-level `Name` property exists, it must not override the shell map key.
- Existing feature configuration validation still applies to `Features`.

### Relationships

- Belongs to exactly one `Shell Configuration Map` entry.
- Produces one runtime shell settings object when composed.
- Contains zero or more `Feature Configuration Entry` values.

## Shell Name

The stable shell identifier derived from the map key.

### Fields

- `Value`: non-empty string from the immediate child key under `CShells:Shells`.
- `ConfigurationPath`: the key path `CShells:Shells:{Value}`.

### Validation Rules

- Must be non-empty after trimming.
- Must not be derived from an inner property.
- Recommended JSON/configuration convention is PascalCase, for example `Default` or `MyShell`.
- Environment variable examples may use uppercase and underscores where required by deployment conventions.

### Relationships

- Exposed through runtime shell settings, blueprint names, shell summaries, descriptors, and registry operations.
- Used in environment override paths.

## Feature Configuration Entry

Represents one configured feature inside a shell definition.

### Fields

- `Name`: feature name from the feature map key.
- `Settings`: feature-specific configuration values.

### Validation Rules

- Existing feature map behavior remains unchanged.
- Feature identity comes from the feature map key in object-map syntax.
- Feature settings flatten under the feature name in shell configuration data.

### Relationships

- Belongs to a `Shell Definition`.
- Contributes to the shell's enabled feature list and feature-specific settings.

## Configuration Layer

Represents a contributing configuration source such as base application settings, deployment settings, or environment variables.

### Fields

- `KeyPaths`: hierarchical configuration paths supplied by the layer.
- `Precedence`: the existing configuration provider ordering that determines final values.

### Validation Rules

- Layers merge by complete key path.
- Later layers may override nested values for an existing named shell.
- Later layers may add new named shell entries.
- Later layers must not depend on array positions to identify shells.

### Relationships

- Multiple layers combine to form the final `Shell Configuration Map`.
- Environment variable layers can target shell settings through named paths such as `CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY__SIGNINGKEY`.

## State Transitions

Configuration data has no independent lifecycle state in this feature. It transitions through the existing pipeline:

1. Configuration providers produce the final named key tree.
2. The shell blueprint provider resolves a named child section.
3. The shell blueprint composes runtime shell settings using the map key as shell name.
4. Existing shell lifecycle components activate or reload shells from those settings.
