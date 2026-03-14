# Data Model: Feature Object Map

## Shell Configuration Model

Represents one configured shell before runtime feature dependency resolution.

### Fields

- `Name`: shell identifier as supplied in configuration
- `Features`: collection of configured feature definitions supplied through array syntax or object-map syntax
- `Configuration`: shell-level configuration outside the feature collection

### Validation Rules

- `Name` must be present and valid per existing `ShellId` rules when transformed into `ShellSettings`.
- `Features` must be expressed as one shape per shell definition: array or object map.
- Ambiguous mixed-shape `Features` input must be rejected before `ShellSettings` creation.

## Feature Definition

Represents one configured feature prior to dependency resolution.

### Fields

- `FeatureName`: canonical feature identifier
- `Settings`: feature-specific configuration values, including nested objects and arrays
- `DeclaredOrder`: the feature’s position in the author-provided configuration order

### Validation Rules

- In array syntax, `FeatureName` comes from the string value or the object `Name` property.
- In object-map syntax, `FeatureName` comes exclusively from the object property key.
- In object-map syntax, all inner properties, including `Name`, belong to `Settings` and do not affect `FeatureName`.
- In object-map syntax, the feature value must be an object; scalar, null, and array values are invalid.

## Feature Collection Representation

Represents the transport shape used to encode the configured feature set.

### Variants

- `ArrayForm`: ordered list containing string feature names and/or objects with `Name` plus settings
- `ObjectMapForm`: ordered set of JSON object properties where each key is a feature name and each value is a settings object

### State Transitions

- `ArrayForm` → `FeatureDefinition[]`: parse each array entry into a normalized feature definition
- `ObjectMapForm` → `FeatureDefinition[]`: parse each object property into a normalized feature definition while preserving property order
- `FeatureDefinition[]` → `ArrayForm` or `ObjectMapForm`: serialize normalized features back to JSON, preferring `ObjectMapForm` when lossless output is possible

## Feature Settings Payload

Represents the configuration values attached to one configured feature.

### Fields

- `SimpleValues`: scalar settings such as strings, numbers, and booleans
- `NestedObjects`: hierarchical settings that must remain bindable through `IConfiguration`
- `Arrays`: ordered lists that must remain intact during JSON and `IConfiguration` processing

### Validation Rules

- Nested settings must survive normalization without losing structure.
- Settings emitted from semantically equivalent array and object-map inputs must flatten to the same shell configuration keys and values.

## Serialization Outcome

Represents the JSON output form produced from normalized feature definitions.

### Fields

- `PreferredShape`: object map when lossless output is possible
- `FallbackShape`: array when object-map output would lose information, such as duplicate feature names

### Validation Rules

- Features without explicit settings serialize as empty-object map entries in preferred object-map output.
- Serialization must preserve configured feature order.
- Serialization must not collapse or overwrite duplicate feature entries silently.