using CShells.AspNetCore.Routing;
using CShells.Lifecycle;
using Microsoft.Extensions.Logging;

namespace CShells.AspNetCore.Routing;

/// <summary>
/// Composes a <see cref="ShellRouteEntry"/> from an <see cref="IShellBlueprint"/> by
/// reading the <c>WebRouting:*</c> keys out of the blueprint's composed
/// <see cref="ShellSettings"/>.
/// </summary>
/// <remarks>
/// Composing the blueprint's settings is the only way to obtain its routing metadata —
/// <see cref="IShellBlueprint"/> intentionally exposes only <c>Name</c>, <c>Metadata</c>,
/// and <c>ComposeAsync</c>, so the routing index pays one
/// <see cref="IShellBlueprint.ComposeAsync"/> call per blueprint per snapshot rebuild.
/// </remarks>
internal static class ShellRouteIndexBuilder
{
    public const string WebRoutingPathKey = "WebRouting:Path";
    public const string WebRoutingHostKey = "WebRouting:Host";
    public const string WebRoutingHeaderNameKey = "WebRouting:HeaderName";
    public const string WebRoutingClaimKeyKey = "WebRouting:ClaimKey";

    /// <summary>
    /// Builds an entry for the supplied blueprint, or returns <c>null</c> when the blueprint
    /// declares no routing configuration or the configuration is invalid.
    /// </summary>
    public static async Task<ShellRouteEntry?> BuildEntryAsync(
        IShellBlueprint blueprint,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Guard.Against.Null(blueprint);
        Guard.Against.Null(logger);

        ShellSettings settings;
        try
        {
            settings = await blueprint.ComposeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Composing settings for blueprint '{Name}' failed; excluding from route index.",
                blueprint.Name);
            return null;
        }

        var path = settings.GetConfiguration(WebRoutingPathKey);
        var host = settings.GetConfiguration(WebRoutingHostKey);
        var headerName = settings.GetConfiguration(WebRoutingHeaderNameKey);
        var claimKey = settings.GetConfiguration(WebRoutingClaimKeyKey);

        // Reject leading-slash paths at index-population time so we never throw on the
        // request hot path. The previous resolver threw InvalidOperationException mid-request.
        if (path is { Length: > 0 } && path[0] == '/')
        {
            logger.LogWarning(
                "Blueprint '{Name}' declares WebRouting:Path '{Path}' which starts with '/' — excluding from path routing. Strip the leading slash to make this blueprint path-routable.",
                blueprint.Name, path);
            path = null;
        }

        // Path-by-name convention: TryMatchByPathSegmentAsync looks up the blueprint via
        // IShellBlueprintProvider.GetAsync(segment), so path-mode requires WebRouting:Path
        // to equal the blueprint's short name (case-insensitively). A non-matching value
        // is silently inert for path routing on the request hot path; warn here so the
        // misconfiguration is discoverable at startup rather than via mysterious 404s.
        if (path is { Length: > 0 } && !string.Equals(path, blueprint.Name, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Blueprint '{Name}' declares WebRouting:Path '{Path}' which differs from the blueprint name. Path-mode routing requires WebRouting:Path to equal the blueprint name (the request's first path segment is looked up directly via IShellBlueprintProvider.GetAsync); this blueprint will not be reachable via path routing. Use the blueprint name as the path, or route via Host / HeaderName / ClaimKey instead.",
                blueprint.Name, path);
        }

        return ShellRouteEntry.TryCreate(blueprint.Name, path, host, headerName, claimKey);
    }
}
