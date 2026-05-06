# Public Contract: Feature Configuration Object Map

## Scope

This contract defines the supported public shapes for the `ShellConfig.Features` configuration value and the normalization rules that convert those shapes into runtime shell feature definitions.

The behavior applies to:

- `IConfiguration`-based shell loading
- Direct JSON deserialization into `ShellConfig`
- Provider integrations that deserialize `ShellConfig` from JSON, including FluentStorage
- JSON serialization of `ShellConfig`

## Accepted Input Shapes

### Array Form

```json
{
  "Name": "Default",
  "Features": [
    "Core",
    { "Name": "Posts" },
    { "Name": "Analytics", "TopPostsCount": 10 }
  ]
}
```

Array object entries may also use a legacy `Settings` wrapper:

```json
{
  "Name": "Default",
  "Features": [
    { "Name": "Analytics", "Settings": { "TopPostsCount": 10 } }
  ]
}
```

### Object-Map Form

```json
{
  "Name": "Default",
  "Features": {
    "Core": {},
    "Posts": {},
    "Analytics": { "TopPostsCount": 10 }
  }
}
```

## Normalization Rules

- Array entries normalize into feature definitions in array order.
- Object-map entries normalize into feature definitions in property declaration order.
- In object-map form, the property key is the only feature identifier.
- In object-map form, every property inside the feature value object, including `Name`, is treated as feature configuration even when blank.
- In object-map form, the feature value must be an object.
- Nested objects and arrays inside feature settings remain part of the feature configuration payload.
- In array object form, `Name` identifies the feature and all other direct properties are settings.
- In legacy array object form, `Settings` may wrap the settings object. `Settings` must not be mixed with direct sibling settings.

## Invalid Input Rules

The following inputs must be rejected:

- A `Features` section that combines array-like numeric children and object-map named children for the same shell
- An object-map entry whose value is `null`, a scalar, or an array
- An array-object entry that omits the `Name` property in array form
- An array entry whose string value is blank
- An array-object entry whose `Name` property is blank
- An array-object entry that mixes a `Settings` wrapper with direct sibling settings
- Any input that cannot be normalized without silently discarding feature configuration data

## Serialization Rules

When serializing `ShellConfig` to JSON:

- The preferred output shape for `Features` is object-map form.
- Features with no explicit settings emit an empty object value, for example `"Core": {}`.
- Configured feature order is preserved in output.
- If object-map output would not be lossless, such as when duplicate feature names exist, serialization may fall back to array form rather than silently collapsing entries.

## Behavioral Equivalence Rules

Semantically equivalent array and object-map inputs must normalize to the same runtime result:

- identical enabled feature names
- identical feature declaration order before dependency resolution
- identical flattened feature configuration values inside `ShellSettings.ConfigurationData`

## Runtime Visibility Rules

- Feature configuration values remain available through the existing shell configuration access pattern used by features.
- This feature does not change feature dependency resolution rules; it only changes how feature definitions are expressed and normalized.
