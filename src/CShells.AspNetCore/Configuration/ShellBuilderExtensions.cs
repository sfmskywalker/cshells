using CShells.Configuration;

namespace CShells.AspNetCore.Configuration;

/// <summary>
/// ASP.NET Core-specific extension methods for <see cref="ShellBuilder"/>.
/// </summary>
public static class ShellBuilderExtensions
{
    /// <summary>
    /// Configures the shell to be resolved by the specified URL path prefix.
    /// </summary>
    /// <param name="builder">The shell builder.</param>
    /// <param name="path">The URL path prefix (e.g., "acme" for /acme/...).</param>
    /// <returns>The builder for method chaining.</returns>
    public static ShellBuilder WithPath(this ShellBuilder builder, string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(path);
        return builder.WithProperty(ShellPropertyKeys.Path, path);
    }

    /// <summary>
    /// Configures the shell to be resolved by the specified hostname.
    /// </summary>
    /// <param name="builder">The shell builder.</param>
    /// <param name="host">The hostname (e.g., "acme.example.com").</param>
    /// <returns>The builder for method chaining.</returns>
    public static ShellBuilder WithHost(this ShellBuilder builder, string host)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(host);
        return builder.WithProperty(ShellPropertyKeys.Host, host);
    }
}
