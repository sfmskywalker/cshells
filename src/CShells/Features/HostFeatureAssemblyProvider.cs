using System.Reflection;

namespace CShells.Features;

internal sealed class HostFeatureAssemblyProvider : IFeatureAssemblyProvider
{
    public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(serviceProvider);
        return Task.FromResult<IEnumerable<Assembly>>(FeatureAssemblyResolver.ResolveHostAssemblies());
    }
}
