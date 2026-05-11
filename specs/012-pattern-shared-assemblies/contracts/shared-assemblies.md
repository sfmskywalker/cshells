# Contract: Host-Wide Shared Assemblies

## Configuration Contract

Host applications declare exact names and prefix wildcard patterns in one root collection:

```json
{
  "CShells": {
    "SharedAssemblies": [
      "Elsa",
      "Elsa.*"
    ],
    "Shells": {
      "Contoso": {
        "Features": [ "Core" ]
      }
    }
  }
}
```

Contract rules:

- `CShells:SharedAssemblies` is host-wide.
- Entries without `*` match exact assembly simple names.
- Entries ending in `*` match assembly simple-name prefixes.
- Entries with `*` anywhere except the final character are invalid.
- Blank or whitespace-only entries are invalid.
- Matching is case-insensitive and uses only assembly simple names.
- `CShells:Shells:<Name>` does not support overriding shared assembly selectors.

## Code-First Builder Contract

The builder exposes host-wide selector APIs:

```csharp
builder.AddShells(cshells => cshells
    .WithSharedAssemblies("Elsa", "Elsa.*")
    .WithSharedAssembliesWhere(name => name.StartsWith("MyFramework.", StringComparison.OrdinalIgnoreCase))
    .WithConfigurationProvider(builder.Configuration));
```

Contract rules:

- `WithSharedAssemblies(params string[] patterns)` appends exact names and prefix wildcard patterns to the host-wide selector collection.
- `WithSharedAssembliesWhere(Func<string, bool> predicate)` appends a code-first predicate selector evaluated against assembly simple names only.
- Null builder arguments, null string arrays, null entries, blank entries, and null predicates fail with argument validation.
- Predicate exceptions fail with actionable feedback identifying the selector source.
- Configuration and code-first selectors compose additively.

## Public Abstractions Contract

Public selector diagnostics live in `CShells.Abstractions`:

```csharp
namespace CShells.Features;

public enum SharedAssemblySelectorKind
{
    Exact,
    PrefixPattern,
    Predicate
}

public sealed record SharedAssemblyMatch(
    string AssemblyName,
    SharedAssemblySelectorKind SelectorKind,
    string? SelectorPattern,
    string SelectorSource);
```

The implementation may expose match diagnostics through an inspectable service or resolver result, but must not require consumers to reference the implementation project to understand selector kinds or match results.

## Matching Contract Examples

| Selector | Candidate Simple Name | Result |
|---|---|---|
| `Elsa` | `Elsa` | Match |
| `Elsa` | `Elsa.Workflows` | No match |
| `Elsa.*` | `Elsa.Workflows` | Match |
| `Elsa.*` | `Contoso.Elsa.Workflows` | No match |
| `elsa.*` | `Elsa.Workflows` | Match |
| `*.Contracts` | `Elsa.Contracts` | Invalid selector |
| `Elsa.*.Abstractions` | `Elsa.Core.Abstractions` | Invalid selector |
