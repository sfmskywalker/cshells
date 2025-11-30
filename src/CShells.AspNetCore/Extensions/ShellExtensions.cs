using System.Reflection;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Resolution;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.AspNetCore.Extensions;

/// <summary>
/// Extension methods for configuring CShells in ASP.NET Core applications.
/// </summary>
public static class ShellExtensions
{
    /// <param name="builder">The <see cref="WebApplicationBuilder"/> to configure.</param>
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Adds CShells core services and ASP.NET Core integration using the default
        /// configuration section "CShells" and the default shell resolver.
        /// </summary>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        public WebApplicationBuilder AddCShells()
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.AddCShells(sectionName: CShellsOptions.SectionName, assemblies: null);
        }

        /// <summary>
        /// Adds CShells core services and ASP.NET Core integration using the default
        /// configuration section "CShells" and the specified feature assemblies.
        /// </summary>
        /// <param name="assemblies">The assemblies to scan for CShells features. If <c>null</c>, all loaded assemblies are scanned.</param>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        public WebApplicationBuilder AddCShells(IEnumerable<Assembly> assemblies)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.AddCShells(sectionName: CShellsOptions.SectionName, assemblies: assemblies);
        }

        /// <summary>
        /// Adds CShells core services and ASP.NET Core integration using the specified
        /// configuration section and optional feature assemblies.
        /// </summary>
        /// <param name="sectionName">The configuration section name to bind CShells options from.</param>
        /// <param name="assemblies">The assemblies to scan for CShells features. If <c>null</c>, all loaded assemblies are scanned.</param>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        public WebApplicationBuilder AddCShells(string sectionName, IEnumerable<Assembly>? assemblies = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(sectionName);

            ConfigureCoreAndAspNetCore(builder, sectionName, assemblies);
            return builder;
        }

        /// <summary>
        /// Adds CShells core services and ASP.NET Core integration, allowing customization
        /// of the shell settings provider and shell resolver.
        /// </summary>
        /// <param name="configureCShells">Callback used to configure the CShells builder (e.g., shell settings provider).</param>
        /// <param name="assemblies">The assemblies to scan for CShells features. If <c>null</c>, all loaded assemblies are scanned.</param>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        public WebApplicationBuilder AddCShells(
            Action<CShellsBuilder> configureCShells,
            IEnumerable<Assembly>? assemblies = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configureCShells);

            // Register CShells core services
            var cshellsBuilder = builder.Services.AddCShells(assemblies);
            configureCShells(cshellsBuilder);

            // Register ASP.NET Core integration for CShells
            builder.Services.AddCShellsAspNetCore();

            return builder;
        }

        /// <summary>
        /// Adds CShells core services and ASP.NET Core integration using the specified
        /// configuration section and optional feature assemblies, with a fluent API for shell resolution.
        /// </summary>
        /// <param name="configureResolvers">Callback used to configure shell resolution strategies.</param>
        /// <param name="sectionName">The configuration section name to bind CShells options from. Defaults to "CShells".</param>
        /// <param name="assemblies">The assemblies to scan for CShells features. If <c>null</c>, all loaded assemblies are scanned.</param>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        public WebApplicationBuilder AddCShells(Action<ShellResolutionBuilder> configureResolvers,
            string sectionName = CShellsOptions.SectionName,
            IEnumerable<Assembly>? assemblies = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configureResolvers);
            ArgumentException.ThrowIfNullOrEmpty(sectionName);

            // Build the custom resolver
            var resolutionBuilder = new ShellResolutionBuilder();
            configureResolvers(resolutionBuilder);

            // Register the resolver BEFORE ConfigureCoreAndAspNetCore so it takes precedence
            // and prevents the default resolver from being added (via TryAddSingleton).
            builder.Services.AddSingleton(resolutionBuilder.Build());

            ConfigureCoreAndAspNetCore(builder, sectionName, assemblies);

            return builder;
        }
    }

    private static void ConfigureCoreAndAspNetCore(
        WebApplicationBuilder builder,
        string sectionName,
        IEnumerable<Assembly>? assemblies)
    {
        var configuration = builder.Configuration;

        // Register CShells core services using configuration provider
        builder.Services.AddCShells(assemblies)
            .WithConfigurationProvider(configuration, sectionName);

        // Register ASP.NET Core integration for CShells (includes default IShellResolver
        // if none has been registered).
        builder.Services.AddCShellsAspNetCore();
    }
}
