# Quickstart: Polymorphic Feature Configuration

## Goal

Verify that per-shell features can be enabled, disabled, and configured with the compact map syntax.

## Example Configuration

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "DefaultAuthentication": true,
          "Identity": false,
          "Http": {
            "HttpActivityOptions": {
              "BasePath": "/workflows",
              "BaseUrl": "http://elsa-server:8080"
            }
          }
        }
      }
    }
  }
}
```

Expected result:

- `DefaultAuthentication` is enabled with defaults.
- `Identity` is disabled.
- `Http` is enabled and receives `HttpActivityOptions` settings directly.

## Docker Override Scenario

Application defaults can enable features:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Identity": {
            "SigningKey": "default"
          },
          "Http": true
        }
      }
    }
  }
}
```

A mounted deployment file can disable defaults:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Identity": false
        }
      }
    }
  }
}
```

Expected result: `Identity` does not participate in dependency resolution, configuration binding, service registration, endpoint mapping, or post-configuration hooks.

## Environment Variable Scenario

Use string-valued provider syntax:

```text
CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY=false
```

Expected result: string `false` is interpreted case-insensitively as a disable declaration.

## Validation Checklist

Run focused tests during implementation:

```bash
dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~Configuration"
dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~Lifecycle.Blueprints"
dotnet test tests/CShells.Tests/
```

Verify:

- `true`, `"true"`, `{}`, and object entries enable features.
- `false` and `"false"` disable features.
- Higher-priority `true` resets lower-priority option values.
- Object entries preserve direct settings and normal layered merging.
- Unknown `false` entries do not fail activation.
- Unknown positive entries fail with actionable messages.
- Existing object-map configuration continues to bind feature options without a `Settings` wrapper.
