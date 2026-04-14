using System.Reflection;

namespace CShells.Features;

internal sealed class ExplicitFeatureAssemblyProvider : IFeatureAssemblyProvider
{
    private readonly IReadOnlyList<Assembly> _assemblies;

    public ExplicitFeatureAssemblyProvider(IEnumerable<Assembly> assemblies)
    {
        var configuredAssemblies = Guard.Against.Null(assemblies).ToArray();

        _assemblies = configuredAssemblies.Select((assembly, index) => assembly ?? throw new ArgumentException($"Explicit feature assembly at index {index} cannot be null.", nameof(assemblies)))
            .ToArray();
    }

    public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(serviceProvider);
        return Task.FromResult<IEnumerable<Assembly>>(_assemblies);
    }
}
