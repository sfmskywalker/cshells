using CShells.Lifecycle;
using CShells.Resolution;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.AspNetCore.Resolution;

/// <summary>
/// Resolves the request's target shell by inspecting each active shell's
/// <c>WebRouting:*</c> configuration keys — path, host, header, claim, or root path.
/// </summary>
[ResolverOrder(0)]
public class WebRoutingShellResolver(
    IShellRegistry registry,
    WebRoutingShellResolverOptions options) : IShellResolverStrategy
{
    private readonly IShellRegistry _registry = Guard.Against.Null(registry);
    private readonly WebRoutingShellResolverOptions _options = Guard.Against.Null(options);

    /// <inheritdoc />
    public ShellId? Resolve(ShellResolutionContext context)
    {
        Guard.Against.Null(context);
        return TryResolveByPath(context)
            ?? TryResolveByHost(context)
            ?? TryResolveByHeader(context)
            ?? TryResolveByClaim(context)
            ?? TryResolveByRootPath();
    }

    private IEnumerable<IShell> ActiveShells() => _registry.GetActiveShells();

    private ShellId? TryResolveByPath(ShellResolutionContext context)
    {
        if (!_options.EnablePathRouting)
            return null;

        var path = context.Get<string>(ShellResolutionContextKeys.Path);
        if (string.IsNullOrEmpty(path) || path.Length <= 1)
            return null;

        if (_options.ExcludePaths is { Length: > 0 } excludePaths &&
            excludePaths.Any(excluded => path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var pathValue = path.AsSpan(1);
        var slashIndex = pathValue.IndexOf('/');
        var firstSegment = slashIndex >= 0 ? pathValue[..slashIndex].ToString() : pathValue.ToString();

        return FindMatchingShell(firstSegment, "Path");
    }

    private ShellId? TryResolveByHost(ShellResolutionContext context)
    {
        if (!_options.EnableHostRouting)
            return null;

        var host = context.Get<string>(ShellResolutionContextKeys.Host);
        return string.IsNullOrEmpty(host) ? null : FindMatchingShell(host, "Host");
    }

    private ShellId? TryResolveByHeader(ShellResolutionContext context)
    {
        if (string.IsNullOrWhiteSpace(_options.HeaderName))
            return null;

        var headerValue = context.Get<string>($"Header:{_options.HeaderName}");
        return string.IsNullOrEmpty(headerValue)
            ? null
            : FindMatchingShellByIdentifier(headerValue, _options.HeaderName, "HeaderName");
    }

    private ShellId? TryResolveByClaim(ShellResolutionContext context)
    {
        if (string.IsNullOrWhiteSpace(_options.ClaimKey))
            return null;

        var claimValue = context.Get<string>($"Claim:{_options.ClaimKey}");
        return string.IsNullOrEmpty(claimValue)
            ? null
            : FindMatchingShellByIdentifier(claimValue, _options.ClaimKey, "ClaimKey");
    }

    private ShellId? FindMatchingShell(string valueToMatch, string configKey)
    {
        foreach (var shell in ActiveShells())
        {
            var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
            var routeValue = settings.GetConfiguration($"WebRouting:{configKey}");

            if (routeValue?.StartsWith('/') == true)
                throw new InvalidOperationException($"Web routing path cannot start with a slash: '{routeValue}'");

            if (!string.IsNullOrEmpty(routeValue) && routeValue.Equals(valueToMatch, StringComparison.OrdinalIgnoreCase))
                return new ShellId(shell.Descriptor.Name);
        }
        return null;
    }

    private ShellId? FindMatchingShellByIdentifier(string identifierValue, string configuredKey, string configKey)
    {
        foreach (var shell in ActiveShells())
        {
            var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
            var shellConfigKey = settings.GetConfiguration($"WebRouting:{configKey}");
            if (string.IsNullOrEmpty(shellConfigKey))
                continue;

            if (shellConfigKey.Equals(configuredKey, StringComparison.OrdinalIgnoreCase) &&
                identifierValue.Equals(shell.Descriptor.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new ShellId(shell.Descriptor.Name);
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the single shell whose <c>WebRouting:Path</c> is set to the empty string,
    /// indicating it opts into root-level routing. Ambiguous configuration (multiple shells
    /// opting in) returns <c>null</c> so the next strategy can decide.
    /// </summary>
    private ShellId? TryResolveByRootPath()
    {
        if (!_options.EnablePathRouting)
            return null;

        ShellId? rootShellId = null;

        foreach (var shell in ActiveShells())
        {
            var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
            var routeValue = settings.GetConfiguration("WebRouting:Path");

            if (routeValue is not { Length: 0 })
                continue;

            if (rootShellId.HasValue)
                return null;

            rootShellId = new ShellId(shell.Descriptor.Name);
        }

        return rootShellId;
    }
}
