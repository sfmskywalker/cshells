using System.Reflection;
using Microsoft.Extensions.DependencyModel;

namespace CShells.Features;

internal static class FeatureAssemblyResolver
{
    public static IReadOnlyCollection<Assembly> ResolveAssemblies(IEnumerable<IFeatureAssemblyProvider> providers, IServiceProvider serviceProvider)
        => ResolveAssembliesAsync(providers, serviceProvider).ConfigureAwait(false).GetAwaiter().GetResult();

    public static async Task<IReadOnlyCollection<Assembly>> ResolveAssembliesAsync(
        IEnumerable<IFeatureAssemblyProvider> providers,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var assemblyProviders = Guard.Against.Null(providers).ToArray();
        Guard.Against.Null(serviceProvider);

        var resolvedAssemblies = new List<Assembly>();
        var seenAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in assemblyProviders)
        {
            var assemblyProvider = Guard.Against.Null(provider);
            var contributedAssemblies = await assemblyProvider.GetAssembliesAsync(serviceProvider, cancellationToken)
                ?? throw new InvalidOperationException($"The feature assembly provider '{assemblyProvider.GetType().FullName}' returned null. Providers must return a non-null sequence.");

            foreach (var assembly in contributedAssemblies)
            {
                if (assembly is null)
                    throw new InvalidOperationException($"The feature assembly provider '{assemblyProvider.GetType().FullName}' returned a null assembly entry. Providers must return only non-null assemblies.");

                if (seenAssemblies.Add(GetAssemblyIdentity(assembly)))
                    resolvedAssemblies.Add(assembly);
            }
        }

        return resolvedAssemblies.AsReadOnly();
    }

    public static IReadOnlyCollection<Assembly> ResolveHostAssemblies(Func<AssemblyName, bool>? filter = null)
    {
        var entry = Assembly.GetEntryAssembly();
        var names = new HashSet<AssemblyName>(new AssemblyNameComparer());

        if (entry is not null)
            names.Add(entry.GetName());

        var dependencyContext = DependencyContext.Default;
        if (dependencyContext is not null)
        {
            foreach (var runtimeLibrary in dependencyContext.RuntimeLibraries)
            {
                foreach (var assemblyName in runtimeLibrary.GetDefaultAssemblyNames(dependencyContext))
                    names.Add(assemblyName);
            }
        }

        if (filter is not null)
            names.RemoveWhere(name => !filter(name));

        var loadedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(assemblyName))
                loadedAssemblies[assemblyName] = assembly;
        }

        var result = new List<Assembly>();
        foreach (var name in names)
        {
            if (name.Name is null)
                continue;

            if (loadedAssemblies.TryGetValue(name.Name, out var alreadyLoaded))
            {
                result.Add(alreadyLoaded);
                continue;
            }

            try
            {
                result.Add(Assembly.Load(name));
            }
            catch
            {
                // Ignore assemblies that cannot be loaded in this process (optional deps, analyzers, etc.)
            }
        }

        return result.AsReadOnly();
    }

    private static string GetAssemblyIdentity(Assembly assembly) => assembly.FullName ?? assembly.GetName().Name ?? assembly.ManifestModule.ModuleVersionId.ToString();

    private sealed class AssemblyNameComparer : IEqualityComparer<AssemblyName>
    {
        public bool Equals(AssemblyName? x, AssemblyName? y) => StringComparer.OrdinalIgnoreCase.Equals(x?.Name, y?.Name);

        public int GetHashCode(AssemblyName obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name ?? string.Empty);
    }
}
