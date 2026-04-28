# Contract Delta: `IShellResolverStrategy` (sync → async)

**Feature**: [010-blueprint-aware-routing](../spec.md)
**Location**: `CShells.AspNetCore.Abstractions/Resolution/IShellResolverStrategy.cs`

The single breaking abstraction change in this feature. `Resolve` becomes `ResolveAsync` because the route index is asynchronous (R-004).

## Before

```csharp
namespace CShells.Resolution;

public interface IShellResolverStrategy
{
    ShellId? Resolve(ShellResolutionContext context);
}
```

## After

```csharp
namespace CShells.Resolution;

public interface IShellResolverStrategy
{
    /// <summary>
    /// Resolves the request's target shell, or returns <c>null</c> to defer to the next
    /// strategy in the resolver pipeline.
    /// </summary>
    /// <remarks>
    /// Implementations that perform purely synchronous work return a completed task
    /// (<c>Task.FromResult&lt;ShellId?&gt;(...)</c>). The pipeline awaits each strategy
    /// in <see cref="ResolverOrderAttribute"/> order and short-circuits on the first
    /// non-null result.
    /// </remarks>
    Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken);
}
```

## Migration for built-in strategies

### `WebRoutingShellResolver`

```csharp
// Before
public ShellId? Resolve(ShellResolutionContext context)
{
    Guard.Against.Null(context);
    return TryResolveByPath(context)
        ?? TryResolveByHost(context)
        ?? TryResolveByHeader(context)
        ?? TryResolveByClaim(context)
        ?? TryResolveByRootPath();
}

// After
public async Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken)
{
    Guard.Against.Null(context);
    var criteria = BuildCriteria(context);
    try
    {
        var match = await _routeIndex.TryMatchAsync(criteria, cancellationToken).ConfigureAwait(false);
        if (match is not null)
            return match.ShellId;
    }
    catch (ShellRouteIndexUnavailableException ex)
    {
        _logger.LogWarning(ex, "Route index unavailable; falling through to next strategy");
    }

    LogNoMatch(criteria);
    return null;
}
```

The internal `TryResolveByPath` / `TryResolveByHost` / `TryResolveByHeader` / `TryResolveByClaim` / `TryResolveByRootPath` methods and `FindMatchingShell` / `FindMatchingShellByIdentifier` helpers are deleted — their logic moves into `ShellRouteIndexBuilder` (entry construction) and `DefaultShellRouteIndex.TryMatchAsync` (lookup).

### `DefaultShellResolverStrategy`

Trivial migration; the strategy always returns `ShellId("Default")`:

```csharp
// Before
public ShellId? Resolve(ShellResolutionContext context) => new ShellId("Default");

// After
public Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken)
    => Task.FromResult<ShellId?>(new ShellId("Default"));
```

## Migration for custom (third-party) strategies

A custom strategy that today is purely synchronous needs three changes:

1. Method signature: `ShellId? Resolve(ShellResolutionContext context)` → `Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken)`.
2. Return path: wrap the existing synchronous return value in `Task.FromResult<ShellId?>(...)`.
3. Optionally observe `cancellationToken` if the strategy does any I/O.

A strategy that today blocks on async work (an anti-pattern) gets to drop the `.GetAwaiter().GetResult()` and `await` properly.

## Caller migration: `ShellMiddleware`

```csharp
// Before
var shellId = _resolver.Resolve(resolutionContext);

// After
var shellId = await _resolver.ResolveAsync(resolutionContext, context.RequestAborted)
    .ConfigureAwait(false);
```

The resolver pipeline (whatever orchestrates the `[ResolverOrder]`-sorted strategy collection — confirmed during implementation per R-009) becomes async end-to-end. There is no parallel sync pipeline.

## Behavioural compatibility

- Strategies whose pre-feature behaviour returned a non-null result for given inputs continue to do so post-feature, with the single exception called out in `spec.md` FR-006: blueprints that were previously invisible to `WebRoutingShellResolver` because they were not yet active become visible to the route index and produce non-null results.
- Strategies whose pre-feature behaviour returned `null` continue to return `null` (the route index does not introduce false-positive matches).
- The `[ResolverOrder]`-driven pipeline ordering is unchanged.

## Why not retain a sync facade?

Three options were considered (R-004):

1. **Async-only contract (chosen)**: one keyword to migrate per custom strategy.
2. **Sync-as-default-interface-method calling async**: forces async work onto the implementer of the new method while preserving the sync method as a lie. Rejected.
3. **Sync as canonical, async as opt-in**: the resolver pipeline still has to await *something*, so the sync method becomes vestigial within months. Rejected.

The constitution (Principle VI) explicitly permits breaking changes that improve API quality. This is one.
