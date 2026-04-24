# Contract: `ProvidedBlueprint`

**Feature**: [007-blueprint-provider-split](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/ProvidedBlueprint.cs`

Pairs a blueprint with its optional owning manager. Returned by
`IShellBlueprintProvider.GetAsync`.

## Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// A blueprint paired with the manager that owns its underlying source (if any).
/// </summary>
/// <param name="Blueprint">The blueprint. Required.</param>
/// <param name="Manager">
/// The owning manager, or <c>null</c> when the source is read-only (e.g., a
/// configuration-file provider).
/// </param>
public sealed record ProvidedBlueprint(IShellBlueprint Blueprint, IShellBlueprintManager? Manager = null);
```

## Invariants

- `Blueprint` is never null.
- When `Manager` is non-null, `Manager.Owns(Blueprint.Name)` MUST return `true`.
- `Blueprint.Name` matches the name that was passed to `IShellBlueprintProvider.GetAsync`
  under case-insensitive ordinal comparison.

## Usage

The registry treats `ProvidedBlueprint` as an immutable value. Callers MUST NOT mutate
the blueprint or manager references after a `ProvidedBlueprint` is returned — both are
cached by the registry for the lifetime of the resulting active generation.
