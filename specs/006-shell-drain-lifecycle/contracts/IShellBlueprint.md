# Contract: IShellBlueprint

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IShellBlueprint` is the re-invocable source-of-truth for how to compose a shell of a given
name. Exactly one blueprint is registered per name. Every `ActivateAsync` and `ReloadAsync`
call invokes `ComposeAsync` to produce a fresh `ShellSettings` that the registry consumes
to build the next generation.

## Interface Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// A re-invocable recipe for composing a single named shell's <see cref="ShellSettings"/>.
/// </summary>
/// <remarks>
/// Blueprints hold no runtime state. They are registered once per shell name via
/// <see cref="IShellRegistry.RegisterBlueprint"/> (or the fluent <c>AddShell(name, ...)</c>
/// shorthand on <c>CShellsBuilder</c>) and invoked repeatedly by the registry on every
/// activation and reload.
/// </remarks>
public interface IShellBlueprint
{
    /// <summary>
    /// The shell name this blueprint produces settings for.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Static metadata copied onto every generation's <see cref="ShellDescriptor.Metadata"/>.
    /// May be empty; never null.
    /// </summary>
    IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Composes a fresh <see cref="ShellSettings"/> for this shell name.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token passed from the registry.</param>
    /// <returns>
    /// A <see cref="ShellSettings"/> whose <c>Id.Name</c> equals <see cref="Name"/>.
    /// </returns>
    /// <remarks>
    /// Must be re-invocable; must not mutate shared external state.
    /// Exceptions propagate out of <see cref="IShellRegistry.ActivateAsync"/> and
    /// <see cref="IShellRegistry.ReloadAsync"/>; the registry discards any partial work.
    /// </remarks>
    Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default);
}
```

## Built-in Implementations

### DelegateShellBlueprint

Produced by `CShellsBuilder.AddShell(name, Action<ShellBuilder>)`. Invokes the stored
delegate against a fresh `ShellBuilder(name)` on every `ComposeAsync`, returning the built
`ShellSettings`. Metadata comes from an optional overload.

### ConfigurationShellBlueprint

Binds an `IConfigurationSection` (or a named `ShellConfig`) on every `ComposeAsync`, so
edits to the underlying configuration source are picked up on the next reload. Metadata
can be sourced from a dedicated `metadata:` section in the same configuration.

## Behaviour Contract

- The registry validates `ComposeAsync`'s result: if the returned `ShellSettings.Id.Name`
  does not match `Name`, the registry throws `InvalidOperationException`.
- `ComposeAsync` may be asynchronous (IO-bound config reads) but should complete promptly.
- `Metadata` is snapshot-copied onto each generation's `ShellDescriptor`; mutating the
  blueprint's backing dictionary after the fact does not retroactively change existing
  descriptors.
