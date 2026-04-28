namespace CShells.AspNetCore.Routing;

/// <summary>
/// Identifies which <c>WebRouting:*</c> configuration value produced a route match.
/// Surfaced on <see cref="ShellRouteMatch"/> for diagnostics and tests.
/// </summary>
public enum ShellRoutingMode
{
    /// <summary>The first URL path segment matched a blueprint's <c>WebRouting:Path</c> value.</summary>
    Path,

    /// <summary>The request URL is the root and exactly one blueprint opted into root-path routing via <c>WebRouting:Path = ""</c>.</summary>
    RootPath,

    /// <summary>The HTTP host header matched a blueprint's <c>WebRouting:Host</c> value.</summary>
    Host,

    /// <summary>The configured request header's value matched a blueprint's name and the blueprint declared <c>WebRouting:HeaderName</c>.</summary>
    Header,

    /// <summary>The configured user claim's value matched a blueprint's name and the blueprint declared <c>WebRouting:ClaimKey</c>.</summary>
    Claim,
}
