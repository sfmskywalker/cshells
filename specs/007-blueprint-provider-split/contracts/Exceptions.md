# Contract: Exception Types

**Feature**: [007-blueprint-provider-split](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/`

Four new exception types, all extending `InvalidOperationException` to stay consistent
with feature `006`'s exception hierarchy. Each carries structured context so callers
(and future admin API) can distinguish and handle them without string matching.

## `ShellBlueprintNotFoundException`

```csharp
namespace CShells.Lifecycle;

public sealed class ShellBlueprintNotFoundException : InvalidOperationException
{
    public string Name { get; }

    public ShellBlueprintNotFoundException(string name)
        : base($"No blueprint is registered for shell '{name}'. " +
               "Check the composite provider's registered sources, or call a manager's " +
               "CreateAsync to add one.")
    {
        Name = Guard.Against.NullOrWhiteSpace(name);
    }
}
```

Thrown by:
- `IShellRegistry.GetOrActivateAsync` when the provider returns `null`.
- `IShellRegistry.ActivateAsync` and `ReloadAsync` when the provider returns `null`.
- `IShellRegistry.UnregisterBlueprintAsync` when the provider returns `null`.

## `ShellBlueprintUnavailableException`

```csharp
public sealed class ShellBlueprintUnavailableException : InvalidOperationException
{
    public string Name { get; }

    public ShellBlueprintUnavailableException(string name, Exception inner)
        : base($"Blueprint lookup for shell '{name}' failed; the underlying source " +
               "is unavailable. See inner exception for details. The call may be " +
               "retried once the source is healthy again.",
               inner)
    {
        Name = Guard.Against.NullOrWhiteSpace(name);
    }
}
```

Thrown by:
- `IShellRegistry.GetOrActivateAsync` when the provider throws during lookup. The inner
  exception MUST be the provider's original fault; callers inspect it for retry
  decisions.

## `BlueprintNotMutableException`

```csharp
public sealed class BlueprintNotMutableException : InvalidOperationException
{
    public string Name { get; }
    public string? SourceId { get; }

    public BlueprintNotMutableException(string name, string? sourceId = null)
        : base(BuildMessage(name, sourceId))
    {
        Name = Guard.Against.NullOrWhiteSpace(name);
        SourceId = sourceId;
    }

    private static string BuildMessage(string name, string? sourceId) =>
        sourceId is null
            ? $"Blueprint '{name}' has no registered manager; its source is read-only. " +
              "Register an IShellBlueprintManager for this name's owning provider to enable mutation."
            : $"Blueprint '{name}' is owned by '{sourceId}', which is read-only. " +
              "Register an IShellBlueprintManager for '{sourceId}' to enable mutation.";
}
```

Thrown by:
- `IShellRegistry.UnregisterBlueprintAsync` when the blueprint's owning provider has no
  manager.
- Future admin-API write paths when they receive a `null` manager from
  `IShellRegistry.GetManagerAsync`.

## `DuplicateBlueprintException`

```csharp
public sealed class DuplicateBlueprintException : InvalidOperationException
{
    public string Name { get; }
    public Type FirstProviderType { get; }
    public Type SecondProviderType { get; }

    public DuplicateBlueprintException(string name, Type firstProviderType, Type secondProviderType)
        : base($"Shell name '{name}' is claimed by both '{firstProviderType.Name}' and " +
               $"'{secondProviderType.Name}'. Each shell name must be owned by exactly one " +
               "blueprint provider.")
    {
        Name = Guard.Against.NullOrWhiteSpace(name);
        FirstProviderType = Guard.Against.Null(firstProviderType);
        SecondProviderType = Guard.Against.Null(secondProviderType);
    }
}
```

Thrown by:
- `CompositeShellBlueprintProvider` when a lookup under the duplicate-detection code
  path finds two sub-providers both returning a non-null `ProvidedBlueprint` for the
  same name.
- `CompositeShellBlueprintProvider.ListAsync` when a name appears twice across
  sub-provider pages.

## HTTP response mapping (future feature 009)

For reference, feature `009` (admin API) will translate these to HTTP responses:

| Exception | HTTP status | Body |
|-----------|-------------|------|
| `ShellBlueprintNotFoundException` | `404 Not Found` | `{ "error": "NotFound", "name": "..." }` |
| `ShellBlueprintUnavailableException` | `503 Service Unavailable` | `{ "error": "Unavailable", "name": "..." }` |
| `BlueprintNotMutableException` | `409 Conflict` | `{ "error": "NotMutable", "name": "...", "sourceId": "..." }` |
| `DuplicateBlueprintException` | `500 Internal Server Error` | `{ "error": "Duplicate", "name": "...", "first": "...", "second": "..." }` |

Feature `007`'s ASP.NET Core middleware handles the first two (for the first-request
activation path); the latter two are admin-API concerns in feature `009`.
