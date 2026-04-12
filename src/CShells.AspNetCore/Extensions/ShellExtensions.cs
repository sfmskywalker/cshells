using CShells.Configuration;
using CShells.DependencyInjection;
using Microsoft.AspNetCore.Builder;

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
        public WebApplicationBuilder AddShells()
        {
            return builder.AddShells(sectionName: CShellsOptions.SectionName);
        }

        /// <summary>
        /// Adds CShells core services and ASP.NET Core integration using the default
        /// configuration section "CShells" and the default shell resolver.
        /// </summary>
        /// <param name="sectionName">The configuration section name to bind CShells options from.</param>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        /// <remarks>
        /// Configure feature discovery assemblies through the fluent <see cref="CShellsBuilder"/> APIs when needed.
        /// </remarks>
        public WebApplicationBuilder AddShells(string sectionName)
        {
            Guard.Against.NullOrEmpty(sectionName);

            return builder.AddShells(shells => shells.WithConfigurationProvider(builder.Configuration, sectionName));
        }

        /// <summary>
        /// Adds CShells core services and ASP.NET Core integration, allowing customization
        /// of the shell settings provider, feature assembly sources, and shell resolver.
        /// </summary>
        /// <param name="configureCShells">Callback used to configure the CShells builder.</param>
        /// <returns>The same <see cref="WebApplicationBuilder"/> instance for chaining.</returns>
        /// <remarks>
        /// Configure feature discovery assemblies through <c>FromAssemblies(...)</c>, <c>FromHostAssemblies()</c>,
        /// or <c>WithAssemblyProvider(...)</c> inside <paramref name="configureCShells"/>.
        /// </remarks>
        public WebApplicationBuilder AddShells(Action<CShellsBuilder> configureCShells)
        {
            Guard.Against.Null(configureCShells);

            // Register ASP.NET Core integration for CShells
            builder.Services.AddCShellsAspNetCore(configureCShells);

            return builder;
        }
    }
}
