using CShells.AspNetCore.Resolution;
using CShells.DependencyInjection;
using CShells.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CShells.AspNetCore.Configuration;

/// <summary>
/// Extension methods for <see cref="CShellsBuilder"/> to configure ASP.NET Core-specific shell resolution.
/// </summary>
public static class CShellsBuilderExtensions
{
    /// <summary>
    /// Automatically registers shell resolution strategies based on shell properties.
    /// This method scans all configured shells for Path and Host properties and registers
    /// appropriate resolver strategies.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <returns>The builder for method chaining.</returns>
    public static CShellsBuilder WithAutoResolvers(this CShellsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var shells = builder.GetShells();

        // Collect path and host mappings from shell properties
        var pathMappings = new Dictionary<string, ShellId>(StringComparer.OrdinalIgnoreCase);
        var hostMappings = new Dictionary<string, ShellId>(StringComparer.OrdinalIgnoreCase);

        foreach (var shell in shells)
        {
            // Check for path property
            if (shell.Properties.TryGetValue(ShellPropertyKeys.Path, out var pathValue)
                && pathValue is string path)
            {
                pathMappings[path] = shell.Id;
            }

            // Check for host property
            if (shell.Properties.TryGetValue(ShellPropertyKeys.Host, out var hostValue)
                && hostValue is string host)
            {
                hostMappings[host] = shell.Id;
            }
        }

        // Register path resolver if any path mappings exist
        if (pathMappings.Count > 0)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IShellResolverStrategy>(
                    new PathShellResolver(pathMappings)));
        }

        // Register host resolver if any host mappings exist
        if (hostMappings.Count > 0)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IShellResolverStrategy>(
                    new HostShellResolver(hostMappings)));
        }

        return builder;
    }

    /// <summary>
    /// Manually registers path-based shell resolution for the specified shells.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="pathMappings">Dictionary mapping URL path prefixes to shell IDs.</param>
    /// <returns>The builder for method chaining.</returns>
    public static CShellsBuilder WithPathResolver(
        this CShellsBuilder builder,
        IReadOnlyDictionary<string, ShellId> pathMappings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pathMappings);

        if (pathMappings.Count > 0)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IShellResolverStrategy>(
                    new PathShellResolver(pathMappings)));
        }

        return builder;
    }

    /// <summary>
    /// Manually registers host-based shell resolution for the specified shells.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="hostMappings">Dictionary mapping hostnames to shell IDs.</param>
    /// <returns>The builder for method chaining.</returns>
    public static CShellsBuilder WithHostResolver(
        this CShellsBuilder builder,
        IReadOnlyDictionary<string, ShellId> hostMappings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(hostMappings);

        if (hostMappings.Count > 0)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IShellResolverStrategy>(
                    new HostShellResolver(hostMappings)));
        }

        return builder;
    }
}
