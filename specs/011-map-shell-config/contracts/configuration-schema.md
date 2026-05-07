# Contract: Map-Based Shell Configuration

## Supported `CShells:Shells` Shape

`CShells:Shells` is an object whose property names are shell names. The property value is the shell definition.

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Configuration": {
          "WebRouting": {
            "Path": ""
          }
        },
        "Features": {
          "DefaultAdminUser": {
            "AdminPassword": "password"
          },
          "Identity": {
            "SigningKey": "..."
          }
        }
      }
    }
  }
}
```

## Shell Name Contract

- The shell name is the immediate child key under `CShells:Shells`.
- The shell definition does not need a shell-level `Name` property.
- A shell-level `Name` property is not a supported identity override.
- Blank shell keys are invalid.
- Numeric shell keys are invalid because they indicate the removed array shape.

## Unsupported Shape

The previous array shape is not supported.

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": {
          "Identity": {
            "SigningKey": "..."
          }
        }
      }
    ]
  }
}
```

Implementations must reject this shape before shell activation with an actionable error naming `CShells:Shells`.

## Environment Variable Contract

Environment variable paths target named shells instead of array indices.

```text
CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY__SIGNINGKEY=test
```

This path targets:

```text
CShells:Shells:Default:Features:Identity:SigningKey
```

For shell names with multiple words, use PascalCase in JSON and the same shell-name segment in environment variables (casing may differ, but do not add underscores within the shell name). Example:

```json
{
  "CShells": {
    "Shells": {
      "MyShell": {
        "Features": {
          "Identity": {}
        }
      }
    }
  }
}
```

```text
CSHELLS__SHELLS__MYSHELL__FEATURES__IDENTITY__SIGNINGKEY=test
```

## Layered Merge Contract

Given a base layer:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Configuration": {
          "Plan": "Free",
          "WebRouting": {
            "Path": ""
          }
        },
        "Features": {
          "Identity": {
            "SigningKey": "base"
          }
        }
      }
    }
  }
}
```

And a later layer:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Configuration": {
          "Plan": "Enterprise"
        }
      },
      "Contoso": {
        "Configuration": {
          "WebRouting": {
            "Path": "contoso"
          }
        },
        "Features": {
          "Identity": {}
        }
      }
    }
  }
}
```

The final configuration contains:

- `Default` with `Plan` overridden to `Enterprise`.
- `Default` retaining `WebRouting:Path` and `Identity:SigningKey` from the base layer.
- `Contoso` added as a second shell.

No array position participates in shell identity or merging.
