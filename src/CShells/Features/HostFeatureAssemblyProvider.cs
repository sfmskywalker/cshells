using System.Reflection;

namespace CShells.Features;

internal sealed class HostFeatureAssemblyProvider : IFeatureAssemblyProvider
{
    public IEnumerable<Assembly> GetAssemblies(IServiceProvider serviceProvider)
    {
        Guard.Against.Null(serviceProvider);
        return FeatureAssemblyResolver.ResolveHostAssemblies();
    }
}
