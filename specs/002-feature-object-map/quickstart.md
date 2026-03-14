# Quickstart: Validate Feature Object Map Support

## Prerequisites

- The feature branch `002-feature-object-map` is checked out.
- The existing configuration-related test suite builds and passes before changes begin.
- A sample shell configuration is available through `appsettings.json` or a JSON-backed provider.

## 1. Add object-map parsing to configuration loading

- Update the `Features` section parsing used by `ShellSettingsFactory.CreateFromConfiguration`.
- Accept either array form or object-map form.
- Reject ambiguous mixed-shape `Features` sections explicitly.

## 2. Add object-map support to direct JSON deserialization

- Extend the `ShellConfig.Features` JSON conversion boundary so object-map JSON deserializes into the existing normalized feature list.
- Keep array-form deserialization behavior unchanged.
- Ensure object-map entry order is preserved.

## 3. Preserve runtime configuration behavior

- Keep feature configuration flattening compatible with the existing shell configuration access path.
- Ensure object-map entries with nested objects and arrays remain available to features.
- Ensure an inner `Name` property in object-map syntax is treated as feature configuration, not as feature identity.

## 4. Prefer object-map serialization

- Update `ShellConfig` JSON serialization so `Features` emits object-map syntax when lossless output is possible.
- Emit empty objects for features that have no explicit settings.
- Fall back to array output only when required to avoid data loss.

## 5. Update tests

- Add unit tests for object-map deserialization, serialization, invalid input handling, and ordering behavior.
- Add or extend settings-factory tests for semantic equivalence between array and object-map inputs.
- Add integration tests for `IConfiguration` binding of object-map syntax and mixed-shape rejection.

## 6. Update docs and samples

- Add object-map examples to configuration documentation.
- Update the Workbench sample configuration to demonstrate the supported syntax, if doing so does not reduce coverage of the legacy array examples.

## 7. Validate behavior

- Run focused configuration unit tests.
- Run focused configuration integration tests.
- Run the full CShells unit and integration suite if the focused tests pass.

```bash
dotnet test tests/CShells.Tests/
```

## Expected Outcomes

- Existing array-based `Features` configurations continue to work unchanged.
- Object-map `Features` definitions produce the same runtime shell feature set and configuration values as equivalent array inputs.
- Invalid mixed-shape or invalid-value inputs fail with actionable errors.
- JSON serialization prefers object-map syntax and preserves configured feature order.