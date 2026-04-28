# Contract: Exceptions

**Feature**: [010-blueprint-aware-routing](../spec.md)
**Location**: `CShells.AspNetCore.Abstractions/Routing/`

One new exception type is introduced.

## `ShellRouteIndexUnavailableException`

Raised by `IShellRouteIndex.TryMatchAsync` when the route index has never been successfully populated and the underlying provider is currently failing.

```csharp
namespace CShells.AspNetCore.Routing;

/// <summary>
/// The route index has never been successfully populated and the underlying provider is
/// currently failing. Resolvers SHOULD catch this and return <c>null</c> so the request
/// gets a clean 404 from non-shell middleware rather than a 500.
/// </summary>
/// <remarks>
/// <para>
/// This exception is raised ONLY when the index is in its initial-population state and
/// the provider exception leaves it with no usable snapshot. Subsequent failures during
/// incremental refresh DO NOT raise this exception — the previous good snapshot remains
/// active per <c>spec.md</c> FR-012.
/// </para>
/// <para>
/// Path-by-name lookups are NOT degraded by this exception, because they consult the
/// provider directly (no snapshot required). The exception only affects root-path, host,
/// header, and claim modes during initial population.
/// </para>
/// </remarks>
public sealed class ShellRouteIndexUnavailableException : Exception
{
    public ShellRouteIndexUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

### Construction

- Always constructed with an `innerException` (the provider exception that defeated the initial population). The message includes the provider type name for diagnosability.
- Carries no shell name — the failure is global to the index, not specific to one blueprint.

### Caller behaviour

- `WebRoutingShellResolver` catches the exception in `ResolveAsync`, logs at `Warning` (with the inner exception), and returns `null`. The middleware continues to the next strategy or to a clean 404.
- Custom resolver strategies SHOULD follow the same pattern. The exception is not meant to propagate to ASP.NET Core middleware as an unhandled exception (it would translate to a 500-class response, which is incorrect: the request didn't fail; the routing layer just doesn't know yet).
- `ShellMiddleware` does NOT catch this exception; if a custom resolver swallows it but rethrows (or chooses to bubble), it will reach the standard ASP.NET Core exception-handler middleware as a 500. This is acceptable — the exception type is documented and the recommended catch sites are clear.

## Why not reuse `ShellBlueprintUnavailableException`?

The `ShellBlueprintUnavailableException` from feature `007` describes a single named blueprint's unavailability (the registry's `GetOrActivateAsync` couldn't reach the provider for *this* name). The route index failure is global — initial population needed the catalogue and couldn't get it. Reusing the per-name exception type would force a synthetic shell name and obscure the actual failure mode.

## Why not just return `null` from `TryMatchAsync` instead of throwing?

`null` from `TryMatchAsync` means "no match." Returning `null` for an index-unavailable case would silently degrade routing to "every request 404s" with no diagnostic surface. The exception is explicit, type-checkable, and forces resolvers to acknowledge the degraded state in their catch site. Resolvers that *do* want to silently return `null` may catch the exception and do so — but the choice is theirs.
