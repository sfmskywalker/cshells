# Contract: Feature Configuration Values

## Supported JSON Map Shape

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

## Value Semantics

| Value form | Meaning |
|------------|---------|
| `true` | Enable the feature with default settings and ignore inherited lower-priority settings for that feature. |
| `"true"` | Same as `true`; accepted case-insensitively for string-valued providers. |
| `false` | Disable the feature, even if lower-priority configuration or code-first defaults enabled it. |
| `"false"` | Same as `false`; accepted case-insensitively for string-valued providers. |
| `{}` | Enable the feature with default settings; retained for existing object-map compatibility. |
| `{ ... }` | Enable the feature and bind object properties directly as feature settings. |

## Invalid Values

These forms must fail before shell activation:

```json
{
  "Features": {
    "Identity": null,
    "Http": "yes",
    "Posts": 1,
    "Analytics": []
  }
}
```

## Direct Feature Settings

Feature object properties bind directly under the feature name. A property named `Enabled` is feature settings, not control metadata.

```json
{
  "Features": {
    "Example": {
      "Enabled": true,
      "Limit": 10
    }
  }
}
```

The example above enables `Example` and exposes settings equivalent to:

```text
Example:Enabled = true
Example:Limit = 10
```

## Layered Override Examples

Base application defaults:

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

Mounted deployment override:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Identity": false,
          "Http": {
            "HttpActivityOptions": {
              "BasePath": "/custom"
            }
          }
        }
      }
    }
  }
}
```

Result:

- `Identity` is disabled.
- `Http` remains enabled and receives merged object settings.

Environment-style override:

```text
CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY=false
```

The string value `false` disables `Identity`.

## Unknown Features

| Declaration | Result |
|-------------|--------|
| Unknown feature set to `false` | Allowed no-op disablement. |
| Unknown feature set to `true` | Invalid attempted activation. |
| Unknown feature set to object | Invalid attempted activation. |

## Code-First Defaults

Code-first feature registrations are defaults. Higher-priority configuration can disable or re-enable them using the same value semantics.
