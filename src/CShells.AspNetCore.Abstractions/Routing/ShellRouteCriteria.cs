namespace CShells.AspNetCore.Routing;

/// <summary>
/// The routing inputs extracted from a single request, supplied by
/// <c>WebRoutingShellResolver</c> to <see cref="IShellRouteIndex.TryMatchAsync"/>. Immutable.
/// </summary>
/// <param name="PathFirstSegment">
/// The first URL path segment with no leading slash (e.g. <c>"acme"</c> from
/// <c>"/acme/posts"</c>). <c>null</c> when path routing is disabled or the path is root or
/// excluded; the root case is signalled by <see cref="IsRootPath"/>.
/// </param>
/// <param name="IsRootPath">
/// <c>true</c> when the request URL is <c>/</c> (or equivalent) and path routing is enabled;
/// the index then queries the root-path table (blueprints with <c>WebRouting:Path = ""</c>)
/// rather than the path-segment lookup.
/// </param>
/// <param name="Host">
/// The request <c>HttpContext.Request.Host.Host</c>. <c>null</c> when host routing is
/// disabled.
/// </param>
/// <param name="HeaderName">
/// The configured header name from <c>WebRoutingShellResolverOptions.HeaderName</c>.
/// <c>null</c> when header routing is disabled.
/// </param>
/// <param name="HeaderValue">
/// The value of the configured request header. <c>null</c> when header routing is disabled
/// or when the request does not carry the header.
/// </param>
/// <param name="ClaimKey">
/// The configured claim key from <c>WebRoutingShellResolverOptions.ClaimKey</c>. <c>null</c>
/// when claim routing is disabled.
/// </param>
/// <param name="ClaimValue">
/// The value of the configured claim on the authenticated user principal. <c>null</c> when
/// claim routing is disabled or when the user lacks the claim.
/// </param>
public sealed record ShellRouteCriteria(
    string? PathFirstSegment,
    bool IsRootPath,
    string? Host,
    string? HeaderName,
    string? HeaderValue,
    string? ClaimKey,
    string? ClaimValue);
