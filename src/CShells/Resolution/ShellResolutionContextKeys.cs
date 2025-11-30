namespace CShells.Resolution;

/// <summary>
/// Defines well-known keys for <see cref="ShellResolutionContext"/> data.
/// </summary>
public static class ShellResolutionContextKeys
{
    /// <summary>
    /// The request path (e.g., "/admin/users").
    /// Expected type: <see cref="string"/>
    /// </summary>
    public const string Path = "Path";

    /// <summary>
    /// The host name (e.g., "example.com" or "tenant1.example.com").
    /// Expected type: <see cref="string"/>
    /// </summary>
    public const string Host = "Host";

    /// <summary>
    /// A dictionary of headers from the request.
    /// Expected type: <see cref="IDictionary{TKey,TValue}"/> where TKey is <see cref="string"/> and TValue is <see cref="string"/> or <see cref="IEnumerable{T}"/> of <see cref="string"/>.
    /// </summary>
    public const string Headers = "Headers";

    /// <summary>
    /// A dictionary of query string parameters or message properties.
    /// Expected type: <see cref="IDictionary{TKey,TValue}"/> where TKey is <see cref="string"/> and TValue is <see cref="string"/> or <see cref="IEnumerable{T}"/> of <see cref="string"/>.
    /// </summary>
    public const string Parameters = "Parameters";

    /// <summary>
    /// The authenticated user principal.
    /// Expected type: <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// </summary>
    public const string User = "User";

    /// <summary>
    /// The subdomain extracted from the host (e.g., "tenant1" from "tenant1.example.com").
    /// Expected type: <see cref="string"/>
    /// </summary>
    public const string Subdomain = "Subdomain";

    /// <summary>
    /// The client IP address.
    /// Expected type: <see cref="string"/>
    /// </summary>
    public const string IpAddress = "IpAddress";

    /// <summary>
    /// A routing key (e.g., from message queue systems).
    /// Expected type: <see cref="string"/>
    /// </summary>
    public const string RoutingKey = "RoutingKey";

    /// <summary>
    /// The raw protocol-specific context object (e.g., HttpContext, IBasicProperties).
    /// This allows resolvers to access protocol-specific details when needed.
    /// Expected type: <see cref="object"/>
    /// </summary>
    public const string ProtocolContext = "ProtocolContext";
}
