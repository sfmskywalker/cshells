using CShells.Configuration;
using CShells.Hosting;
using CShells.Resolution;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.AspNetCore.Resolution;

/// <summary>
/// A unified shell resolver strategy that supports multiple routing methods:
/// URL path, HTTP host, custom headers, and user claims.
/// </summary>
[ResolverOrder(0)]
public class WebRoutingShellResolver : IShellResolverStrategy
{
    private readonly IShellHost _shellHost;
    private readonly WebRoutingShellResolverOptions _options;

    [ActivatorUtilitiesConstructor]
    public WebRoutingShellResolver(IShellHost shellHost, WebRoutingShellResolverOptions options)
    {
        _shellHost = Guard.Against.Null(shellHost);
        _options = Guard.Against.Null(options);
    }

    /// <summary>
    /// Initializes a new resolver against a static shell settings cache.
    /// This overload exists for compatibility with tests and non-DI callers; the runtime pipeline
    /// should prefer the <see cref="IShellHost"/>-based constructor so only applied shells participate.
    /// </summary>
    public WebRoutingShellResolver(IShellSettingsCache cache, WebRoutingShellResolverOptions options)
        : this(new CacheBackedShellHost(cache), options)
    {
    }

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

    private ShellId? TryResolveByPath(ShellResolutionContext context)
    {
        if (!_options.EnablePathRouting)
            return null;

        var path = context.Get<string>(ShellResolutionContextKeys.Path);
        if (string.IsNullOrEmpty(path) || path.Length <= 1)
            return null;

        if (_options.ExcludePaths is { Length: > 0 } excludePaths)
        {
            if (excludePaths.Any(excludedPath => path.StartsWith(excludedPath, StringComparison.OrdinalIgnoreCase)))
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
        return string.IsNullOrEmpty(headerValue) ? null : FindMatchingShellByIdentifier(headerValue, _options.HeaderName, "HeaderName");
    }

    private ShellId? TryResolveByClaim(ShellResolutionContext context)
    {
        if (string.IsNullOrWhiteSpace(_options.ClaimKey))
            return null;

        var claimValue = context.Get<string>($"Claim:{_options.ClaimKey}");
        return string.IsNullOrEmpty(claimValue) ? null : FindMatchingShellByIdentifier(claimValue, _options.ClaimKey, "ClaimKey");
    }

    private ShellId? FindMatchingShell(string valueToMatch, string configKey)
    {
        foreach (var shell in _shellHost.AllShells)
        {
            var routeValue = shell.Settings.GetConfiguration($"WebRouting:{configKey}");
            
            // If the path starts with a slash, throw a configuration exception:
            if (routeValue?.StartsWith('/') == true)
                throw new($"Web routing path cannot start with a slash: '{routeValue}'");
            
            if (!string.IsNullOrEmpty(routeValue) && routeValue.Equals(valueToMatch, StringComparison.OrdinalIgnoreCase))
                return shell.Id;
        }
        return null;
    }

    private ShellId? FindMatchingShellByIdentifier(string identifierValue, string configuredKey, string configKey)
    {
        foreach (var shell in _shellHost.AllShells)
        {
            var shellConfigKey = shell.Settings.GetConfiguration($"WebRouting:{configKey}");
            if (string.IsNullOrEmpty(shellConfigKey))
                continue;

            if (shellConfigKey.Equals(configuredKey, StringComparison.OrdinalIgnoreCase) &&
                identifierValue.Equals(shell.Id.Name, StringComparison.OrdinalIgnoreCase))
            {
                return shell.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the shell whose <c>WebRouting:Path</c> is explicitly set to an empty string
    /// (<c>""</c>), indicating it is a root-level shell that should handle requests not matched
    /// by any path-prefixed, host-based, header-based, or claim-based resolver.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is intentionally the last resort within this strategy: it only fires after all
    /// other resolution methods have returned <see langword="null"/>.
    /// </para>
    /// <para>
    /// The key distinction between <c>null</c> and <c>""</c> for <c>WebRouting:Path</c>:
    /// <list type="bullet">
    ///   <item><description><c>null</c> / not set — the shell is not path-routed at all; this method ignores it.</description></item>
    ///   <item><description><c>""</c> (explicit empty string) — the shell opts in to root-level routing and becomes the fallback for unmatched requests.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If more than one shell has <c>WebRouting:Path = ""</c> the configuration is ambiguous
    /// and <see langword="null"/> is returned, allowing <see cref="DefaultShellResolverStrategy"/>
    /// to act as the final fallback.
    /// </para>
    /// </remarks>
    private ShellId? TryResolveByRootPath()
    {
        if (!_options.EnablePathRouting)
            return null;

        ShellId? rootShellId = null;

        foreach (var shell in _shellHost.AllShells)
        {
            var routeValue = shell.Settings.GetConfiguration("WebRouting:Path");

            // Only match a shell that explicitly set WebRouting:Path = "" (not null/absent).
            // A null value means the shell simply has no path routing configured.
            if (routeValue is not { Length: 0 })
                continue;

            if (rootShellId.HasValue)
            {
                // Ambiguous: two shells both claim the root path. Return null so that the
                // DefaultShellResolverStrategy can act as the final fallback instead.
                return null;
            }

            rootShellId = shell.Id;
        }

        return rootShellId;
    }

    private sealed class CacheBackedShellHost(IShellSettingsCache cache) : IShellHost
    {
        // Empty providers hold no resources; disposal is a no-op.
        private static readonly IServiceProvider emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

        private readonly IShellSettingsCache cache = Guard.Against.Null(cache);
        private readonly object allShellsLock = new();
        private IReadOnlyCollection<ShellContext>? allShells;

        public ShellContext DefaultShell => AllShells.FirstOrDefault()
            ?? throw new InvalidOperationException("No shells are configured.");

        public IReadOnlyCollection<ShellContext> AllShells => GetAllShells();

        public ShellContext GetShell(ShellId id) => AllShells.FirstOrDefault(shell => shell.Id.Equals(id))
            ?? throw new KeyNotFoundException($"Shell '{id}' was not found.");

        public ValueTask EvictShellAsync(ShellId shellId)
        {
            InvalidateAllShells();
            return ValueTask.CompletedTask;
        }

        public ValueTask EvictAllShellsAsync()
        {
            InvalidateAllShells();
            return ValueTask.CompletedTask;
        }

        private IReadOnlyCollection<ShellContext> GetAllShells()
        {
            lock (allShellsLock)
            {
                return allShells ??= cache.GetAll()
                    .Select(settings => new ShellContext(settings, emptyServiceProvider, settings.EnabledFeatures))
                    .ToList()
                    .AsReadOnly();
            }
        }

        private void InvalidateAllShells()
        {
            lock (allShellsLock)
            {
                allShells = null;
            }
        }
    }
}
