using CShells.AspNetCore.Resolution;
using CShells.Resolution;

namespace CShells.AspNetCore.Extensions;

/// <summary>
/// Extension methods for <see cref="ShellResolutionBuilder"/> providing HTTP-specific resolution strategies.
/// </summary>
public static class ShellResolutionBuilderExtensions
{
    private const string PathMappingsKey = "CShells.AspNetCore.PathMappings";
    private const string HostMappingsKey = "CShells.AspNetCore.HostMappings";
    private const string FinalizerRegisteredKey = "CShells.AspNetCore.FinalizerRegistered";

    /// <param name="builder">The shell resolution builder.</param>
    extension(ShellResolutionBuilder builder)
    {
        /// <summary>
        /// Maps a URL path segment to a specific shell.
        /// </summary>
        /// <param name="path">The path segment (e.g. "admin").</param>
        /// <param name="shellId">The shell identifier.</param>
        /// <returns>The builder for method chaining.</returns>
        public ShellResolutionBuilder MapPath(string path, string shellId)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentException.ThrowIfNullOrEmpty(shellId);

            EnsureFinalizerRegistered(builder);

            var mappings = builder.GetOrCreateProperty(PathMappingsKey, () => new Dictionary<string, ShellId>(StringComparer.OrdinalIgnoreCase));
            mappings[path] = new(shellId);

            return builder;
        }

        /// <summary>
        /// Maps multiple URL path segments to their corresponding shells.
        /// </summary>
        /// <param name="pathMappings">A dictionary mapping path segments to shell identifiers.</param>
        /// <returns>The builder for method chaining.</returns>
        public ShellResolutionBuilder MapPaths(IDictionary<string, string> pathMappings)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(pathMappings);

            EnsureFinalizerRegistered(builder);

            var mappings = builder.GetOrCreateProperty(PathMappingsKey, () => new Dictionary<string, ShellId>(StringComparer.OrdinalIgnoreCase));
            foreach (var (path, shellId) in pathMappings)
            {
                mappings[path] = new(shellId);
            }

            return builder;
        }

        /// <summary>
        /// Maps a host name to a specific shell.
        /// </summary>
        /// <param name="host">The host name (e.g. "example.com").</param>
        /// <param name="shellId">The shell identifier.</param>
        /// <returns>The builder for method chaining.</returns>
        public ShellResolutionBuilder MapHost(string host, string shellId)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(host);
            ArgumentException.ThrowIfNullOrEmpty(shellId);

            EnsureFinalizerRegistered(builder);

            var mappings = builder.GetOrCreateProperty(HostMappingsKey, () => new Dictionary<string, ShellId>(StringComparer.OrdinalIgnoreCase));
            mappings[host] = new(shellId);

            return builder;
        }

        /// <summary>
        /// Maps multiple host names to their corresponding shells.
        /// </summary>
        /// <param name="hostMappings">A dictionary mapping host names to shell identifiers.</param>
        /// <returns>The builder for method chaining.</returns>
        public ShellResolutionBuilder MapHosts(IDictionary<string, string> hostMappings)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(hostMappings);

            EnsureFinalizerRegistered(builder);

            var mappings = builder.GetOrCreateProperty(HostMappingsKey, () => new Dictionary<string, ShellId>(StringComparer.OrdinalIgnoreCase));
            foreach (var (host, shellId) in hostMappings)
            {
                mappings[host] = new(shellId);
            }

            return builder;
        }

        /// <summary>
        /// Sets the default shell to use when no other resolver matches.
        /// </summary>
        /// <param name="shellId">The shell identifier.</param>
        /// <returns>The builder for method chaining.</returns>
        public ShellResolutionBuilder UseDefault(string shellId)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(shellId);

            return builder.AddStrategy(new FixedShellResolver(new(shellId)));
        }
    }

    /// <summary>
    /// Ensures that the HTTP resolver finalizer is registered with the builder.
    /// The finalizer converts accumulated mappings into resolvers during Build().
    /// </summary>
    private static void EnsureFinalizerRegistered(ShellResolutionBuilder builder)
    {
        // Use a marker property to ensure we only register the finalizer once
        if (!builder.TryGetProperty<bool>(FinalizerRegisteredKey, out _))
        {
            builder.GetOrCreateProperty(FinalizerRegisteredKey, () => true);
            builder.AddFinalizer(FinalizeHttpResolvers);
        }
    }

    /// <summary>
    /// Finalizes HTTP-specific mappings and adds them as strategies.
    /// This method is automatically called by GetStrategies() via the finalizer.
    /// </summary>
    private static void FinalizeHttpResolvers(ShellResolutionBuilder builder)
    {
        // Add host resolver if there are host mappings
        if (builder.TryGetProperty<Dictionary<string, ShellId>>(HostMappingsKey, out var hostMappings) && hostMappings is not null && hostMappings.Count > 0)
        {
            builder.AddStrategy(new HostShellResolver(hostMappings));
            builder.RemoveProperty(HostMappingsKey);
        }

        // Add path resolver if there are path mappings
        if (builder.TryGetProperty<Dictionary<string, ShellId>>(PathMappingsKey, out var pathMappings) && pathMappings is not null && pathMappings.Count > 0)
        {
            builder.AddStrategy(new PathShellResolver(pathMappings));
            builder.RemoveProperty(PathMappingsKey);
        }
    }
}
