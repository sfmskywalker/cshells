using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CShells.AspNetCore;

/// <summary>
/// Defines the contract for shell startup configuration classes.
/// </summary>
public interface IShellStartup
{
    /// <summary>
    /// Configures the services for the shell.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Configures the application pipeline for the shell.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="environment">The hosting environment.</param>
    void Configure(IApplicationBuilder app, IHostEnvironment environment);
}
