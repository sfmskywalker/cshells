using System.Reflection;

namespace CShells.Features;

/// <summary>
/// Provides assemblies that CShells should scan for shell features.
/// </summary>
/// <remarks>
/// <para>
/// Implementations participate in CShells feature discovery alongside the built-in host and explicit assembly providers.
/// </para>
/// <para>
/// The supplied <c>serviceProvider</c> argument is the root application service provider used during shell registration.
/// Implementations may return an empty sequence, but they must not return <see langword="null"/>.
/// </para>
/// </remarks>
public interface IFeatureAssemblyProvider
{
    /// <summary>
    /// Gets the assemblies that should be scanned for shell features.
    /// </summary>
    /// <param name="serviceProvider">The root application service provider.</param>
    /// <returns>A non-null sequence of assemblies to scan.</returns>
    IEnumerable<Assembly> GetAssemblies(IServiceProvider serviceProvider);
}
