# Quickstart: Map-Based Shell Configuration

## 1. Configure shells as a map

Use shell names as keys under `CShells:Shells`.

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Configuration": {
          "WebRouting": {
            "Path": ""
          },
          "Plan": "Enterprise"
        },
        "Features": {
          "DefaultAdminUser": {
            "AdminPassword": "password"
          },
          "Identity": {
            "SigningKey": "local-dev-key"
          }
        }
      },
      "Contoso": {
        "Configuration": {
          "WebRouting": {
            "Path": "contoso"
          },
          "Plan": "Standard"
        },
        "Features": {
          "Identity": {
            "SigningKey": "contoso-local-key"
          }
        }
      }
    }
  }
}
```

Do not include a shell-level `Name` property. `Default` and `Contoso` are the shell names.

## 2. Override one shell with an environment variable

Set an environment variable that names the target shell in the path.

```bash
export CSHELLS__SHELLS__DEFAULT__FEATURES__IDENTITY__SIGNINGKEY=test
```

Expected result:

- The `Default` shell's `Identity:SigningKey` is `test`.
- The `Contoso` shell's `Identity:SigningKey` remains `contoso-local-key`.
- Reordering shell entries in JSON does not change which shell is targeted.

## 3. Merge layered configuration by shell name

Base application settings:

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

Deployment settings:

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
          "Identity": {
            "SigningKey": "contoso"
          }
        }
      }
    }
  }
}
```

Expected result:

- `Default:Configuration:Plan` is `Enterprise`.
- `Default:Configuration:WebRouting:Path` remains from the base layer.
- `Default:Features:Identity:SigningKey` remains from the base layer unless separately overridden.
- `Contoso` is added as a separate shell.

## 4. Verify unsupported array syntax fails

This shape is unsupported:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": {
          "Identity": {}
        }
      }
    ]
  }
}
```

Expected result:

- Shell loading fails before activation.
- The error identifies `CShells:Shells`.
- The error explains that shell entries must be named map entries.

## 5. Naming convention

Use PascalCase shell keys in JSON:

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

Use the environment's preferred uppercase/underscore form when writing environment variables:

```bash
export CSHELLS__SHELLS__MY_SHELL__FEATURES__IDENTITY__SIGNINGKEY=test
```

The configuration system maps the environment variable path back to the named shell path.

## 6. Suggested verification commands

```bash
dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~ConfigurationShellBlueprintProviderTests"
dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~Configuration"
dotnet test tests/CShells.Tests/
dotnet build
```
