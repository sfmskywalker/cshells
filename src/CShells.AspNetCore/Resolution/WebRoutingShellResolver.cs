using System.Collections.Immutable;
using System.Security.Claims;
using System.Text;
using CShells.AspNetCore.Routing;
using CShells.Resolution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.AspNetCore.Resolution;

/// <summary>
/// Resolves the request's target shell by consulting the blueprint-backed
/// <see cref="IShellRouteIndex"/>. Path / host / header / claim / root-path matching uses
/// blueprint metadata directly, so blueprints are visible to routing whether or not their
/// shells are currently active. The middleware then activates matched shells lazily via
/// <see cref="CShells.Lifecycle.IShellRegistry.GetOrActivateAsync"/>.
/// </summary>
[ResolverOrder(0)]
public class WebRoutingShellResolver(
    IShellRouteIndex routeIndex,
    WebRoutingShellResolverOptions options,
    ILogger<WebRoutingShellResolver>? logger = null) : IShellResolverStrategy
{
    private readonly IShellRouteIndex _routeIndex = Guard.Against.Null(routeIndex);
    private readonly WebRoutingShellResolverOptions _options = Guard.Against.Null(options);
    private readonly ILogger<WebRoutingShellResolver> _logger = logger ?? NullLogger<WebRoutingShellResolver>.Instance;

    /// <inheritdoc />
    public async Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(context);

        var criteria = BuildCriteria(context);

        // No mode contributes any criterion (e.g. all routing disabled). Skip silently —
        // the next strategy in the pipeline decides.
        if (!HasAnyCriterion(criteria))
            return null;

        ShellRouteMatch? match;
        try
        {
            match = await _routeIndex.TryMatchAsync(criteria, cancellationToken).ConfigureAwait(false);
        }
        catch (ShellRouteIndexUnavailableException ex)
        {
            _logger.LogWarning(ex,
                "Route index unavailable for path '{Path}'; falling through to next resolver strategy.",
                context.Get<string>(ShellResolutionContextKeys.Path));
            return null;
        }

        if (match is not null)
        {
            if (_options.LogMatches)
                _logger.LogDebug("Resolved shell '{Shell}' (mode: {Mode}) for path '{Path}'.",
                    match.ShellId.Name, match.MatchedMode,
                    context.Get<string>(ShellResolutionContextKeys.Path));
            return match.ShellId;
        }

        LogNoMatch(criteria);
        return null;
    }

    private ShellRouteCriteria BuildCriteria(ShellResolutionContext context)
    {
        string? pathFirstSegment = null;
        var isRootPath = false;

        if (_options.EnablePathRouting)
        {
            // Only consider root-path or path-segment matching when the resolution context
            // actually carries a Path value. Non-HTTP contexts (e.g. message-queue resolvers)
            // and tests that don't populate Path leave it null, and we don't want to treat
            // a missing key as an implicit root-path request.
            var path = context.Get<string>(ShellResolutionContextKeys.Path);
            if (path is not null && !IsExcludedPath(path))
            {
                if (path.Length == 0 || path == "/")
                {
                    isRootPath = true;
                }
                else
                {
                    var span = path.AsSpan();
                    if (span[0] == '/')
                        span = span[1..];

                    if (span.Length == 0)
                    {
                        isRootPath = true;
                    }
                    else
                    {
                        var slashIndex = span.IndexOf('/');
                        pathFirstSegment = (slashIndex >= 0 ? span[..slashIndex] : span).ToString();
                    }
                }
            }
        }

        string? host = null;
        if (_options.EnableHostRouting)
            host = context.Get<string>(ShellResolutionContextKeys.Host);

        string? headerValue = null;
        if (!string.IsNullOrWhiteSpace(_options.HeaderName))
            headerValue = context.Get<string>($"Header:{_options.HeaderName}");

        string? claimValue = null;
        if (!string.IsNullOrWhiteSpace(_options.ClaimKey))
        {
            claimValue = context.Get<string>($"Claim:{_options.ClaimKey}");

            // The legacy resolver also supported reading claims directly off the
            // ClaimsPrincipal in the resolution context. Preserve that fallback so consumers
            // that populate the principal but not the colon-keyed claim still work.
            if (claimValue is null && context.Get<ClaimsPrincipal>(ShellResolutionContextKeys.User) is { } user)
                claimValue = user.FindFirst(_options.ClaimKey)?.Value;
        }

        return new ShellRouteCriteria(
            PathFirstSegment: pathFirstSegment,
            IsRootPath: isRootPath,
            Host: host,
            HeaderName: _options.HeaderName,
            HeaderValue: headerValue,
            ClaimKey: _options.ClaimKey,
            ClaimValue: claimValue);
    }

    private bool IsExcludedPath(string? path)
    {
        if (path is null || _options.ExcludePaths is not { Length: > 0 } excluded)
            return false;

        foreach (var excludedPath in excluded)
            if (path.StartsWith(excludedPath, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static bool HasAnyCriterion(ShellRouteCriteria criteria) =>
        criteria.PathFirstSegment is { Length: > 0 }
        || criteria.IsRootPath
        || criteria.Host is { Length: > 0 }
        || criteria.HeaderValue is { Length: > 0 }
        || criteria.ClaimValue is { Length: > 0 };

    private void LogNoMatch(ShellRouteCriteria criteria)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
            return;

        // Clamp to a non-negative cap; treat 0 as "log the no-match line but omit candidates".
        var cap = Math.Max(0, _options.NoMatchLogCandidateCap);
        var snapshot = cap == 0
            ? ImmutableArray<ShellRouteEntry>.Empty
            : _routeIndex.GetCandidateSnapshot(cap + 1);
        var truncated = snapshot.Length > cap;
        var visible = truncated ? cap : snapshot.Length;

        var candidatesBuilder = new StringBuilder();
        for (var i = 0; i < visible; i++)
        {
            if (i > 0) candidatesBuilder.Append(", ");
            var entry = snapshot[i];
            candidatesBuilder.Append(entry.ShellName).Append('(');
            var modes = new List<string>();
            if (entry.Path is not null) modes.Add($"Path=\"{entry.Path}\"");
            if (entry.Host is not null) modes.Add($"Host=\"{entry.Host}\"");
            if (entry.HeaderName is not null) modes.Add($"HeaderName=\"{entry.HeaderName}\"");
            if (entry.ClaimKey is not null) modes.Add($"ClaimKey=\"{entry.ClaimKey}\"");
            candidatesBuilder.Append(string.Join("; ", modes));
            candidatesBuilder.Append(')');
        }
        if (truncated)
            candidatesBuilder.Append($" (+{snapshot.Length - cap} more)");

        _logger.LogInformation(
            "No shell matched the request. Considered: PathFirstSegment={PathFirstSegment}, IsRootPath={IsRootPath}, Host={Host}, HeaderName={HeaderName}, HeaderValue={HeaderValue}, ClaimKey={ClaimKey}, ClaimValue={ClaimValue}. Candidate blueprints: [{Candidates}]",
            criteria.PathFirstSegment ?? "(none)",
            criteria.IsRootPath,
            criteria.Host ?? "(none)",
            criteria.HeaderName ?? "(none)",
            criteria.HeaderValue ?? "(none)",
            criteria.ClaimKey ?? "(none)",
            criteria.ClaimValue ?? "(none)",
            candidatesBuilder.ToString());
    }
}
