using System.Reflection;
using CShells.Configuration;
using CShells.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CShells
{
    /// <summary>
    /// ServiceCollection extensions for registering CShells.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers CShells services and returns a builder for further configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="assemblies">Optional assemblies to scan for features. If null, scans all loaded assemblies.</param>
        /// <returns>A CShells builder for further configuration.</returns>
        public static CShellsBuilder AddCShells(
            this IServiceCollection services,
            IEnumerable<Assembly>? assemblies = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            // Register the root service collection accessor as early as possible.
            // This allows the shell host to copy root service registrations into each shell's service collection.
            // Note: The captured 'services' reference remains valid for the lifetime of the application.
            // Because IServiceCollection is mutable, any services added after AddCShells but before shells are built
            // will still be inherited by shells. This subtle behavior is correct but worth documenting for future maintainers.
            services.TryAddSingleton<IRootServiceCollectionAccessor>(
                _ => new RootServiceCollectionAccessor(services));

            // Register IShellHost using the DefaultShellHost.
            // The root IServiceProvider is passed to allow IShellFeature constructors to resolve root-level services.
            // The root IServiceCollection is passed via the accessor to enable service inheritance in shells.
            services.AddSingleton<IShellHost>(sp =>
            {
                var provider = sp.GetRequiredService<IShellSettingsProvider>();
                var logger = sp.GetService<ILogger<DefaultShellHost>>();
                var rootServicesAccessor = sp.GetRequiredService<IRootServiceCollectionAccessor>();
                var assembliesToScan = assemblies ?? AppDomain.CurrentDomain.GetAssemblies();

                // Load shell settings from the provider
                var shellSettings = provider.GetShellSettingsAsync().GetAwaiter().GetResult();
                var shells = ValidateAndConvertToList(shellSettings);

                return new DefaultShellHost(shells, assembliesToScan, rootProvider: sp, rootServicesAccessor, logger);
            });

            // Register the default shell context scope factory.
            services.AddSingleton<IShellContextScopeFactory, DefaultShellContextScopeFactory>();

            return new(services);
        }

        /// <summary>
        /// Validates shell settings for duplicate names and converts to a list.
        /// </summary>
        private static List<ShellSettings> ValidateAndConvertToList(IEnumerable<ShellSettings> shellSettings)
        {
            var shells = shellSettings.ToList();

            if (shells.Count == 0)
            {
                throw new InvalidOperationException("No shells were returned by the shell settings provider.");
            }

            var duplicates = shells
                .GroupBy(s => s.Id.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (duplicates.Length > 0)
            {
                throw new InvalidOperationException($"Duplicate shell name(s) found: {string.Join(", ", duplicates)}");
            }

            return shells;
        }
    }
}
