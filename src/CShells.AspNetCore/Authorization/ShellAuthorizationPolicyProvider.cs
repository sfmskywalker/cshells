using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CShells.AspNetCore.Authorization;

/// <summary>
/// An authorization policy provider that resolves policies from shell-scoped service providers.
/// This enables per-shell authorization policies (e.g., FastEndpoints epPolicy:* policies) to work
/// correctly even though the authorization middleware runs in the root application context.
/// </summary>
/// <remarks>
/// <para>
/// The challenge this solves:
/// - Each shell has its own IServiceProvider with its own AuthorizationOptions instance
/// - FastEndpoints (and other per-shell frameworks) register policies in the shell's AuthorizationOptions
/// - The authorization middleware runs in the root app context and captures the root's IAuthorizationPolicyProvider at startup
/// - Without this provider, the middleware can't find policies registered in shell AuthorizationOptions
/// </para>
/// <para>
/// How it works:
/// - Implements IAuthorizationPolicyProvider to intercept policy lookups
/// - Uses IHttpContextAccessor to get the current request's HttpContext
/// - Resolves IAuthorizationPolicyProvider from HttpContext.RequestServices (which is shell-scoped by ShellMiddleware)
/// - Falls back to the default policy provider for app-level policies
/// </para>
/// </remarks>
public class ShellAuthorizationPolicyProvider(
    IOptions<AuthorizationOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ShellAuthorizationPolicyProvider>? logger = null) : IAuthorizationPolicyProvider
{
    private readonly IAuthorizationPolicyProvider _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);
    private readonly ILogger<ShellAuthorizationPolicyProvider> _logger = logger ?? NullLogger<ShellAuthorizationPolicyProvider>.Instance;

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellPolicyProvider();
        return shellProvider != null ? shellProvider.GetDefaultPolicyAsync() : _fallbackProvider.GetDefaultPolicyAsync();
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellPolicyProvider();
        return shellProvider != null ? shellProvider.GetFallbackPolicyAsync() : _fallbackProvider.GetFallbackPolicyAsync();
    }

    /// <inheritdoc />
    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Try to get from shell provider first (this is where FastEndpoints registers policies)
        var shellProvider = GetShellPolicyProvider();
        if (shellProvider != null)
        {
            var shellPolicy = await shellProvider.GetPolicyAsync(policyName);
            if (shellPolicy != null)
            {
                _logger.LogTrace("Found policy '{PolicyName}' in shell's authorization provider", policyName);
                return shellPolicy;
            }
        }

        // Fall back to root provider
        var rootPolicy = await _fallbackProvider.GetPolicyAsync(policyName);
        if (rootPolicy != null)
        {
            _logger.LogTrace("Found policy '{PolicyName}' in root authorization provider", policyName);
            return rootPolicy;
        }

        _logger.LogDebug("Policy '{PolicyName}' not found in shell or root authorization providers", policyName);
        return null;
    }

    /// <summary>
    /// Gets the authorization policy provider from the current shell's service provider.
    /// </summary>
    /// <returns>The shell's policy provider, or null if not in a shell context.</returns>
    private IAuthorizationPolicyProvider? GetShellPolicyProvider()
    {
        var httpContext = httpContextAccessor.HttpContext;

        // HttpContext.RequestServices is set by ShellMiddleware to the shell's scoped service provider
        var shellProvider = httpContext?.RequestServices.GetService<IAuthorizationPolicyProvider>();

        // Make sure we don't get ourselves in an infinite loop
        if (shellProvider != null && shellProvider.GetType() != typeof(ShellAuthorizationPolicyProvider))
        {
            return shellProvider;
        }

        return null;
    }
}
