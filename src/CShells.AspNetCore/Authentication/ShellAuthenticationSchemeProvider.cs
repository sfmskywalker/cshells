using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CShells.AspNetCore.Authentication;

/// <summary>
/// An authentication scheme provider that resolves schemes from shell-scoped service providers.
/// This enables per-shell authentication schemes (e.g., JWT, API Key) to work correctly even though
/// the authentication middleware runs in the root application context.
/// </summary>
/// <remarks>
/// <para>
/// The challenge this solves:
/// - Each shell has its own IServiceProvider with its own AuthenticationOptions instance
/// - Shells register authentication schemes (JWT, API Key) in their AuthenticationOptions
/// - The authentication middleware runs in the root app context and captures the root's IAuthenticationSchemeProvider at startup
/// - Without this provider, the middleware can't find schemes registered in shell AuthenticationOptions
/// </para>
/// <para>
/// How it works:
/// - Implements IAuthenticationSchemeProvider to intercept scheme lookups
/// - Uses IHttpContextAccessor to get the current request's HttpContext
/// - Resolves IAuthenticationSchemeProvider from HttpContext.RequestServices (which is shell-scoped by ShellMiddleware)
/// - Falls back to the default scheme provider for app-level schemes
/// </para>
/// </remarks>
public class ShellAuthenticationSchemeProvider(
    IOptions<AuthenticationOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ShellAuthenticationSchemeProvider>? logger = null) : IAuthenticationSchemeProvider
{
    private readonly IAuthenticationSchemeProvider _fallbackProvider = new AuthenticationSchemeProvider(options);
    private readonly ILogger<ShellAuthenticationSchemeProvider> _logger = logger ?? NullLogger<ShellAuthenticationSchemeProvider>.Instance;

    /// <inheritdoc />
    public Task<AuthenticationScheme?> GetSchemeAsync(string name)
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        if (shellProvider != null)
        {
            _logger.LogTrace("Looking up authentication scheme '{SchemeName}' in shell provider", name);
            return shellProvider.GetSchemeAsync(name);
        }

        _logger.LogTrace("Looking up authentication scheme '{SchemeName}' in root provider", name);
        return _fallbackProvider.GetSchemeAsync(name);
    }

    /// <inheritdoc />
    public Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        return shellProvider != null ? shellProvider.GetDefaultAuthenticateSchemeAsync() : _fallbackProvider.GetDefaultAuthenticateSchemeAsync();
    }

    /// <inheritdoc />
    public Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        return shellProvider != null ? shellProvider.GetDefaultChallengeSchemeAsync() : _fallbackProvider.GetDefaultChallengeSchemeAsync();
    }

    /// <inheritdoc />
    public Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        return shellProvider != null ? shellProvider.GetDefaultForbidSchemeAsync() : _fallbackProvider.GetDefaultForbidSchemeAsync();
    }

    /// <inheritdoc />
    public Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        return shellProvider != null ? shellProvider.GetDefaultSignInSchemeAsync() : _fallbackProvider.GetDefaultSignInSchemeAsync();
    }

    /// <inheritdoc />
    public Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        return shellProvider != null ? shellProvider.GetDefaultSignOutSchemeAsync() : _fallbackProvider.GetDefaultSignOutSchemeAsync();
    }

    /// <inheritdoc />
    public Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        return shellProvider != null ? shellProvider.GetAllSchemesAsync() : _fallbackProvider.GetAllSchemesAsync();
    }

    /// <inheritdoc />
    public Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync()
    {
        // Try to get from shell provider first
        var shellProvider = GetShellSchemeProvider();
        return shellProvider != null ? shellProvider.GetRequestHandlerSchemesAsync() : _fallbackProvider.GetRequestHandlerSchemesAsync();
    }

    /// <inheritdoc />
    public void AddScheme(AuthenticationScheme scheme)
    {
        // Delegate to fallback - this shouldn't be called at runtime
        _logger.LogWarning("AddScheme called on ShellAuthenticationSchemeProvider - this should only happen during startup configuration");
        _fallbackProvider.AddScheme(scheme);
    }

    /// <inheritdoc />
    public bool TryAddScheme(AuthenticationScheme scheme)
    {
        // Delegate to fallback - this shouldn't be called at runtime
        _logger.LogWarning("TryAddScheme called on ShellAuthenticationSchemeProvider - this should only happen during startup configuration");
        return _fallbackProvider.TryAddScheme(scheme);
    }

    /// <inheritdoc />
    public void RemoveScheme(string name)
    {
        // Delegate to fallback - this shouldn't be called at runtime
        _logger.LogWarning("RemoveScheme called on ShellAuthenticationSchemeProvider - this should only happen during startup configuration");
        _fallbackProvider.RemoveScheme(name);
    }

    /// <summary>
    /// Gets the authentication scheme provider from the current shell's service provider.
    /// </summary>
    /// <returns>The shell's scheme provider, or null if not in a shell context.</returns>
    private IAuthenticationSchemeProvider? GetShellSchemeProvider()
    {
        var httpContext = httpContextAccessor.HttpContext;

        // HttpContext.RequestServices is set by ShellMiddleware to the shell's scoped service provider
        var shellProvider = httpContext?.RequestServices.GetService<IAuthenticationSchemeProvider>();

        // Make sure we don't get ourselves in an infinite loop
        if (shellProvider != null && shellProvider.GetType() != typeof(ShellAuthenticationSchemeProvider))
            return shellProvider;

        return null;
    }
}
