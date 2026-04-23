# Contract: IDrainPolicy

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IDrainPolicy` governs how long a drain waits for handlers, whether extensions are granted, and
how deadline breaches are handled. Three built-in implementations cover all spec use cases.

## Interface Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Governs drain timeout behaviour: initial deadline, extension decisions, and unbounded mode.
/// </summary>
public interface IDrainPolicy
{
    /// <summary>
    /// Gets the initial drain timeout, or <c>null</c> for an unbounded policy.
    /// </summary>
    TimeSpan? InitialTimeout { get; }

    /// <summary>
    /// Gets whether this policy places no deadline on drain operations.
    /// </summary>
    /// <remarks>
    /// When <c>true</c>, a warning is logged before each drain starts. Intended for
    /// development and test environments only.
    /// </remarks>
    bool IsUnbounded { get; }

    /// <summary>
    /// Requests an extension to the current drain deadline.
    /// </summary>
    /// <param name="requested">The duration the handler is requesting.</param>
    /// <param name="granted">The duration the policy actually grants (may be less than requested).</param>
    /// <returns><c>true</c> if any extension was granted; <c>false</c> otherwise.</returns>
    bool TryExtend(TimeSpan requested, out TimeSpan granted);
}
```

## Built-in Implementations

### FixedTimeoutDrainPolicy *(default)*

**Constructor**: `FixedTimeoutDrainPolicy(TimeSpan timeout)`

| Property | Value |
|----------|-------|
| `InitialTimeout` | `timeout` |
| `IsUnbounded` | `false` |
| `TryExtend` | Always returns `false`; `granted = TimeSpan.Zero` |

Default timeout: **30 seconds** (configured at registration, e.g. `new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30))`).

### ExtensibleTimeoutDrainPolicy

**Constructor**: `ExtensibleTimeoutDrainPolicy(TimeSpan initial, TimeSpan cap)`

| Property | Value |
|----------|-------|
| `InitialTimeout` | `initial` |
| `IsUnbounded` | `false` |
| `TryExtend` | Grants up to `cap - elapsed` of the original deadline; returns `false` when cap is reached |

Extensions are tracked cumulatively; the total deadline never exceeds `cap`.

### UnboundedDrainPolicy

**Constructor**: `UnboundedDrainPolicy()`

| Property | Value |
|----------|-------|
| `InitialTimeout` | `null` |
| `IsUnbounded` | `true` |
| `TryExtend` | Always returns `true`; `granted = requested` |

Logs a `Warning`-level message before each drain: *"Shell '{name}' drain policy is unbounded. This is intended for development/test environments only."*

## Configuration

The active policy is registered as `IDrainPolicy` in the host's root `IServiceCollection`:

```csharp
services.AddSingleton<IDrainPolicy>(new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30))); // default
// or
services.AddSingleton<IDrainPolicy>(new ExtensibleTimeoutDrainPolicy(
    initial: TimeSpan.FromSeconds(30),
    cap: TimeSpan.FromMinutes(2)));
// or (dev/test only)
services.AddSingleton<IDrainPolicy>(new UnboundedDrainPolicy());
```

When no `IDrainPolicy` is registered, `ShellRegistry` defaults to
`FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30))`.
