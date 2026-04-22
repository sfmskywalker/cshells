using CShells.Resolution;
using Microsoft.AspNetCore.Http;

namespace CShells.AspNetCore.Extensions;

/// <summary>
/// Extension methods for converting <see cref="HttpContext"/> to <see cref="ShellResolutionContext"/>.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Converts an <see cref="HttpContext"/> to a <see cref="ShellResolutionContext"/> populated
    /// with the request's path, host, headers, query parameters, user, IP, and a reference to
    /// the raw <see cref="HttpContext"/> for protocol-specific resolvers.
    /// </summary>
    public static ShellResolutionContext ToShellResolutionContext(this HttpContext httpContext)
    {
        Guard.Against.Null(httpContext);

        var context = new ShellResolutionContext();

        context.Set(ShellResolutionContextKeys.Path, httpContext.Request.Path.Value ?? string.Empty);
        context.Set(ShellResolutionContextKeys.Host, httpContext.Request.Host.Host);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in httpContext.Request.Headers)
            headers[header.Key] = header.Value.ToString();
        context.Set(ShellResolutionContextKeys.Headers, headers);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in httpContext.Request.Query)
            parameters[param.Key] = param.Value.ToString();
        context.Set(ShellResolutionContextKeys.Parameters, parameters);

        if (httpContext.User?.Identity?.IsAuthenticated == true)
            context.Set(ShellResolutionContextKeys.User, httpContext.User);

        if (httpContext.Connection.RemoteIpAddress != null)
            context.Set(ShellResolutionContextKeys.IpAddress, httpContext.Connection.RemoteIpAddress.ToString());

        context.Set(ShellResolutionContextKeys.ProtocolContext, httpContext);

        return context;
    }
}
