# Contract: `IShellRouteIndex`

**Feature**: [010-blueprint-aware-routing](../spec.md)
**Location**: `CShells.AspNetCore.Abstractions/Routing/IShellRouteIndex.cs`

A read-only mapping from request-side routing identifiers (path segment, host, header value, claim value, root-path opt-in) to a blueprint name. Used by `WebRoutingShellResolver` to resolve a request to a `ShellId` *before* the corresponding shell has been activated. This closes the lazy-activation chicken-and-egg loop described in `spec.md` §Overview.

The index is blueprint-backed (driven by `IShellBlueprintProvider`), not registry-backed. Active vs. inactive shells are equally visible to a route lookup.

## Interface

```csharp
namespace CShells.AspNetCore.Routing;

/// <summary>
/// Maps request-side routing identifiers to a shell blueprint, regardless of activation
/// state. Built from <see cref="CShells.Lifecycle.IShellBlueprintProvider"/> data.
/// </summary>
/// <remarks>
/// <para>
/// The index is the routing-layer companion to <see cref="CShells.Lifecycle.IShellRegistry"/>.
/// Where the registry holds <em>active</em> generations, the index holds the <em>routing
/// metadata</em> of every blueprint the provider knows about — so a request that arrives
/// before the matching shell has been activated can still be resolved, then handed to
/// <see cref="CShells.Lifecycle.IShellRegistry.GetOrActivateAsync"/> by the middleware.
/// </para>
/// <para>
/// Hot-path lookups for path-by-name routing (the common case) cost at most one
/// <see cref="CShells.Lifecycle.IShellBlueprintProvider.GetAsync"/> call. Lookups for
/// root-path, host, header, and claim modes consult an in-memory snapshot that is
/// populated on first use of those modes and refreshed via lifecycle notifications
/// (<c>ShellAdded</c>/<c>ShellRemoved</c>/<c>ShellReloaded</c>); the snapshot is never
/// rebuilt eagerly at startup.
/// </para>
/// <para>
/// Implementations MUST be safe to call concurrently. The default implementation
/// publishes its snapshot via volatile reference swap so concurrent reads always observe
/// either the previous fully-consistent snapshot or the new one — never a partial state.
/// </para>
/// </remarks>
public interface IShellRouteIndex
{
    /// <summary>
    /// Resolves a routing match for the supplied criteria, or <c>null</c> when no
    /// blueprint matches.
    /// </summary>
    /// <param name="criteria">Routing values extracted from the current request by the
    /// resolver. Fields not corresponding to enabled routing modes MUST be left null.</param>
    /// <param name="cancellationToken">Propagated from the request.</param>
    /// <returns>
    /// A <see cref="ShellRouteMatch"/> identifying the matched blueprint and the routing
    /// mode that produced the match, or <c>null</c> when no match exists.
    /// </returns>
    /// <exception cref="ShellRouteIndexUnavailableException">
    /// The index has never been successfully populated and the underlying provider is
    /// currently failing. Resolvers SHOULD catch this and return <c>null</c> so the
    /// request gets a clean 404 from non-shell middleware rather than a 500.
    /// </exception>
    ValueTask<ShellRouteMatch?> TryMatchAsync(ShellRouteCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a bounded snapshot of the route entries the index currently knows about.
    /// Used by <see cref="WebRoutingShellResolver"/> for the no-match diagnostic log entry
    /// (see <see cref="WebRoutingShellResolverOptions.NoMatchLogCandidateCap"/>).
    /// Implementations MUST NOT trigger catalogue enumeration as a side effect of this
    /// call; they return whatever the current snapshot contains (possibly empty).
    /// </summary>
    /// <param name="maxEntries">Hard cap on the number of entries returned. Implementations
    /// MUST honour the cap; entries beyond the cap are simply omitted (the caller's log
    /// formatter is responsible for the "(+N more)" suffix).</param>
    /// <returns>An immutable snapshot of route entries (possibly empty).</returns>
    ImmutableArray<ShellRouteEntry> GetCandidateSnapshot(int maxEntries);
}
```

## Behavioural requirements

### Lookup semantics

- **Path mode (`criteria.PathFirstSegment` non-null, `IsRootPath = false`)**: the index calls `IShellBlueprintProvider.GetAsync(criteria.PathFirstSegment)`. If the provider returns a blueprint whose `WebRouting:Path` value (case-insensitively) equals `PathFirstSegment`, the match is `(ShellId(blueprint.Name), Path)`. If the provider returns `null`, or returns a blueprint whose path does not match (e.g., the blueprint exists but uses a different routing mode), the index returns `null` for path mode. The index then falls through to other modes per the resolver's overall ordering.
- **Root-path mode (`criteria.IsRootPath = true`)**: the index queries its in-memory `RootPathEntry` field. If exactly one blueprint opted into root-path (`WebRouting:Path = ""`), the match is `(ShellId(rootEntry.ShellName), RootPath)`. If zero or multiple blueprints opted in, the index returns `null` (preserving the existing ambiguity-falls-through semantics). Root-path queries DO trigger first-use snapshot population.
- **Host mode (`criteria.Host` non-null)**: the index queries `ByHost.TryGetValue(criteria.Host, out var entry)`. Host mode triggers first-use snapshot population.
- **Header / Claim mode**: the index queries `ByHeaderValue` / `ByClaimValue` using `criteria.HeaderValue` / `criteria.ClaimValue` as the key. The current `WebRoutingShellResolver` semantics require the header/claim *value* to equal the shell descriptor name AND the blueprint to declare the matching `HeaderName`/`ClaimKey`; the index encodes both checks at population time. Header/claim modes trigger first-use snapshot population.

### Snapshot consistency (FR-013)

- A single `TryMatchAsync` call MUST observe a single consistent snapshot for the duration of the call. Concurrent rebuilds MUST NOT cause a single call to see, e.g., the new `ByPathSegment` map paired with the old `RootPathEntry`.
- Readers and writers are sequenced via `Volatile.Read`/`Volatile.Write` of the snapshot reference. No locks on the read path.
- Writers (lifecycle invalidation) serialize via `SemaphoreSlim(1,1)` to prevent two concurrent rebuilds from racing on the underlying provider state.

### Failure handling (FR-012, R-006)

- A provider exception during snapshot rebuild MUST NOT corrupt the previously published snapshot. The exception is caught by `DefaultShellRouteIndex`, logged at `Warning`, and the index continues to serve from the previous snapshot.
- A provider exception during the **initial** population (no previous snapshot exists) MUST cause `TryMatchAsync` for any non-name mode to throw `ShellRouteIndexUnavailableException` until a subsequent invalidation triggers a successful rebuild.
- Path-by-name lookups (`PathFirstSegment` mode) are NOT degraded by index initial-population failures, because that path does not require the snapshot — it consults the provider directly.

### Concurrency

- All public methods MUST be safe to call concurrently from any number of request threads.
- `TryMatchAsync` MUST not allocate on the steady-state hot path beyond the returned `ShellRouteMatch` (when a match exists).
- `GetCandidateSnapshot` is allowed to allocate the returned `ImmutableArray<ShellRouteEntry>` slice.

## Caller responsibilities

- `WebRoutingShellResolver` constructs `ShellRouteCriteria` with the SAME extraction rules its current sync implementation uses (path's first segment with leading slash stripped, host/header/claim values pulled from the request, mode-disable short-circuits).
- `WebRoutingShellResolver` interprets a `null` result as "no match in the built-in routing modes; let the next strategy try."
- `WebRoutingShellResolver` catches `ShellRouteIndexUnavailableException` and treats it as a no-match (logged at `Warning`).

## Lifecycle

- Registered as a singleton in `ServiceCollectionExtensions` alongside `ShellRouteIndexInvalidator` (the `INotificationHandler<ShellAdded>`/`<ShellRemoved>`/`<ShellReloaded>` handler that pokes the index to refresh).
- `ShellRouteIndexInvalidator` is registered as `IShellLifecycleSubscriber` (or whatever the post-006 notification subscriber type is — confirmed during implementation per R-009).
- The default implementation has no startup cost: the snapshot is built on first non-name-mode lookup.

## Out of scope

- Multi-tenant routing strategies beyond path/host/header/claim (e.g., subdomain regex, custom host parsing). Custom routing remains the territory of `IShellResolverStrategy` implementations.
- Per-request hot-reload of the index. Lifecycle-event-driven invalidation is the only mechanism. A request mid-rebuild may observe either the previous or the next snapshot per FR-013.
- Persistence of the snapshot. The index is purely in-memory and rebuilt at process start.
