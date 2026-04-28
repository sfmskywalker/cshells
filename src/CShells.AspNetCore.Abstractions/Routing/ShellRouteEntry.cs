namespace CShells.AspNetCore.Routing;

/// <summary>
/// A single blueprint's contribution to the route index. Read at index-population time from
/// the blueprint's composed <see cref="CShells.ShellSettings"/> by reading the keys
/// <c>WebRouting:Path</c>, <c>WebRouting:Host</c>, <c>WebRouting:HeaderName</c>, and
/// <c>WebRouting:ClaimKey</c> from <see cref="CShells.ShellSettings.ConfigurationData"/>.
/// </summary>
/// <param name="ShellName">
/// The blueprint name. The <see cref="ShellId"/> the resolver returns wraps this value.
/// </param>
/// <param name="Path">
/// The blueprint's <c>WebRouting:Path</c> value. <c>null</c> when the blueprint does not
/// opt into path routing. The empty string indicates root-path routing. Non-empty values
/// MUST NOT begin with <c>/</c>; entries violating this are rejected by
/// <see cref="ShellRouteEntry.TryCreate"/>.
/// </param>
/// <param name="Host">
/// The blueprint's <c>WebRouting:Host</c> value (e.g. <c>acme.example.com</c>). <c>null</c>
/// when the blueprint does not opt into host routing.
/// </param>
/// <param name="HeaderName">
/// The blueprint's <c>WebRouting:HeaderName</c> value. <c>null</c> when the blueprint does
/// not opt into header routing. When non-null, the route index matches requests whose header
/// of the configured name has a value equal to <see cref="ShellName"/>.
/// </param>
/// <param name="ClaimKey">
/// The blueprint's <c>WebRouting:ClaimKey</c> value. <c>null</c> when the blueprint does not
/// opt into claim routing. When non-null, the route index matches requests whose user claim
/// of the configured key has a value equal to <see cref="ShellName"/>.
/// </param>
public sealed record ShellRouteEntry(
    string ShellName,
    string? Path,
    string? Host,
    string? HeaderName,
    string? ClaimKey)
{
    /// <summary>
    /// Constructs a <see cref="ShellRouteEntry"/> after rejecting common misconfigurations.
    /// Returns <c>null</c> when the entry would not be useful (no routing modes opted in)
    /// or when the configuration is invalid (path begins with <c>/</c>).
    /// </summary>
    /// <remarks>
    /// Invalid <c>Path</c> values (leading slash) cause an early <c>null</c> return rather
    /// than throwing — the index logs a warning at population time and excludes the entry
    /// from path routing. The blueprint may still be reachable via host / header / claim
    /// modes if it declares those.
    /// </remarks>
    public static ShellRouteEntry? TryCreate(
        string shellName,
        string? path,
        string? host,
        string? headerName,
        string? claimKey)
    {
        Guard.Against.NullOrWhiteSpace(shellName);

        if (path is { Length: > 0 } && path[0] == '/')
            return null;

        if (path is null && host is null && headerName is null && claimKey is null)
            return null;

        return new ShellRouteEntry(shellName, path, host, headerName, claimKey);
    }
}
