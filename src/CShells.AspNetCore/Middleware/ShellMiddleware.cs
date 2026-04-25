using CShells.AspNetCore.Extensions;
using CShells.AspNetCore.Routing;
using CShells.Lifecycle;
using CShells.Resolution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CShells.AspNetCore.Middleware;

/// <summary>
/// Middleware that resolves the current shell from the request and sets
/// <see cref="HttpContext.RequestServices"/> to a scope built from that shell's provider.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="IShell.BeginScope"/> so the shell's active-scope counter stays accurate for
/// drain coordination: when a reload triggers a drain on the outgoing generation, the scope
/// held by this request keeps the old provider alive until the response has been written.
/// </para>
/// </remarks>
public class ShellMiddleware(
    RequestDelegate next,
    IShellResolver resolver,
    IShellRegistry registry,
    IMemoryCache cache,
    IOptions<ShellMiddlewareOptions> options,
    ILogger<ShellMiddleware>? logger = null)
{
    private readonly RequestDelegate _next = Guard.Against.Null(next);
    private readonly IShellResolver _resolver = Guard.Against.Null(resolver);
    private readonly IShellRegistry _registry = Guard.Against.Null(registry);
    private readonly IMemoryCache _cache = Guard.Against.Null(cache);
    private readonly ShellMiddlewareOptions _options = Guard.Against.Null(options).Value;
    private readonly ILogger<ShellMiddleware> _logger = logger ?? NullLogger<ShellMiddleware>.Instance;

    /// <summary>Invokes the middleware for the current request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var resolutionContext = context.ToShellResolutionContext();

        // Prefer the shell that owns the matched endpoint (set by UseRouting before this middleware).
        // This guarantees that a shell-owned endpoint always executes inside the shell's scope,
        // even when the general resolver pipeline would have picked a different default.
        var endpointShellId = context.GetEndpoint()?.Metadata.GetMetadata<ShellEndpointMetadata>()?.ShellId;

        ShellId? shellId = endpointShellId ?? ResolveShellWithCache(context, resolutionContext);

        if (shellId is null)
        {
            _logger.LogDebug("No shell resolved for request, continuing without shell scope");
            await _next(context);
            return;
        }

        _logger.LogInformation("Resolved shell '{ShellId}' for request path '{Path}'", shellId.Value, context.Request.Path);

        IShell shell;
        try
        {
            // Lazy activation: GetOrActivateAsync returns the active generation if already live,
            // otherwise looks up the blueprint via the provider, builds the shell, and publishes it.
            // Stampede-safe — the per-name semaphore serializes concurrent cold-shell requests.
            shell = await _registry.GetOrActivateAsync(shellId.Value.Name, context.RequestAborted);
        }
        catch (ShellBlueprintNotFoundException)
        {
            _logger.LogInformation("No blueprint registered for shell '{ShellId}'; returning 404.", shellId.Value);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        catch (ShellBlueprintUnavailableException ex)
        {
            _logger.LogWarning(ex, "Blueprint provider unavailable for shell '{ShellId}'; returning 503.", shellId.Value);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        // BeginScope increments the shell's active-scope counter (so in-flight drains' phase-1
        // waits for this request to complete). The scope is released via OnCompleted (not
        // `await using`) so upstream middleware can still read RequestServices during its
        // post-_next processing — releasing at InvokeAsync return could dispose the DI scope
        // out from under that post-processing and cause ObjectDisposedException.
        var scope = shell.BeginScope();
        context.RequestServices = scope.ServiceProvider;
        context.Response.OnCompleted(() => scope.DisposeAsync().AsTask());

        await _next(context);
    }

    private ShellId? ResolveShellWithCache(HttpContext context, ShellResolutionContext resolutionContext)
    {
        if (!_options.EnableCaching)
            return _resolver.Resolve(resolutionContext);

        var cacheKey = BuildCacheKey(context);

        return _cache.GetOrCreate(cacheKey, entry =>
        {
            var resolvedShellId = _resolver.Resolve(resolutionContext);

            if (resolvedShellId is null)
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1);
            }
            else
            {
                entry.SlidingExpiration = _options.CacheSlidingExpiration;
                entry.AbsoluteExpirationRelativeToNow = _options.CacheAbsoluteExpiration;
                entry.Size = 1;
                _logger.LogDebug("Cached shell '{ShellId}' for key '{CacheKey}'", resolvedShellId.Value, cacheKey);
            }

            return resolvedShellId;
        });
    }

    private static string BuildCacheKey(HttpContext context)
    {
        var request = context.Request;
        return $"{request.Host}:{request.Path}:{request.Method}";
    }
}
