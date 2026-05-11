# Quickstart: Pattern-Based Shared Assemblies

## Configuration

Add host-wide shared assembly selectors under root `CShells:SharedAssemblies`:

```json
{
  "CShells": {
    "SharedAssemblies": [
      "Elsa",
      "Elsa.*"
    ],
    "Shells": {
      "Contoso": {
        "Features": [ "Core" ],
        "Configuration": {
          "WebRouting": {
            "Path": "contoso"
          }
        }
      }
    }
  }
}
```

Wire configuration as usual:

```csharp
builder.Services.AddCShells(cshells => cshells
    .WithConfigurationProvider(builder.Configuration));
```

Expected behavior:

- `Elsa` matches only the `Elsa` assembly simple name.
- `Elsa.*` matches `Elsa.Workflows` and other simple names beginning with `Elsa.`.
- `Contoso.Workflows` does not match either selector.
- `*.Contracts` is invalid because `*` is not the final character in a prefix pattern.

## Code-First

Reusable integration packages can contribute the same selectors through the builder:

```csharp
builder.Services.AddCShells(cshells => cshells
    .WithSharedAssemblies("Elsa", "Elsa.*")
    .WithSharedAssembliesWhere(name =>
        name.StartsWith("MyFramework.", StringComparison.OrdinalIgnoreCase)));
```

Configuration and code-first selectors compose additively and produce one shared decision per assembly simple name.

## Verification

Run focused tests while implementing:

```bash
dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~SharedAssembly"
```

Run the complete test suite before handing off:

```bash
dotnet test
```

Documentation updates should show:

- Root `CShells:SharedAssemblies` configuration.
- Exact-name and prefix wildcard examples.
- `WithSharedAssembliesWhere` for code-first predicate selection.
- Guidance that broad shared assembly patterns can weaken shell isolation.
