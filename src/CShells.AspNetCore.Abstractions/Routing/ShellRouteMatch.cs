namespace CShells.AspNetCore.Routing;

/// <summary>
/// The optional result of <see cref="IShellRouteIndex.TryMatchAsync"/>. Identifies the
/// matched blueprint and the routing mode that produced the match.
/// </summary>
/// <param name="ShellId">
/// The matched blueprint's identifier; passed unchanged to
/// <see cref="CShells.Lifecycle.IShellRegistry.GetOrActivateAsync"/> by the middleware.
/// </param>
/// <param name="MatchedMode">
/// Which <see cref="ShellRoutingMode"/> produced the match. Surfaced in the per-match
/// diagnostic log entry and used by tests for precise assertions.
/// </param>
public sealed record ShellRouteMatch(ShellId ShellId, ShellRoutingMode MatchedMode);
