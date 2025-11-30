using Microsoft.Extensions.DependencyInjection;

namespace CShells.DependencyInjection;

/// <summary>
/// Builder for configuring CShells services with a fluent API.
/// </summary>
public class CShellsBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CShellsBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CShellsBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }
}
