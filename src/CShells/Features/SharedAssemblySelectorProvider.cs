using System.Reflection;

namespace CShells.Features;

internal sealed class SharedAssemblySelectorProvider
{
    private readonly IReadOnlyList<ISharedAssemblySelector> selectors;
    private readonly List<SharedAssemblyMatch> matches = [];
    private readonly HashSet<string> matchedAssemblyNames = new(StringComparer.OrdinalIgnoreCase);

    public SharedAssemblySelectorProvider(IEnumerable<ISharedAssemblySelector> selectors)
    {
        this.selectors = Guard.Against.Null(selectors)
            .Select(selector => selector ?? throw new ArgumentException("Shared assembly selector entries cannot be null.", nameof(selectors)))
            .ToArray();
    }

    public bool HasSelectors => selectors.Count > 0;

    public IReadOnlyList<SharedAssemblyMatch> Matches => matches.AsReadOnly();

    public bool IsMatch(AssemblyName assemblyName)
    {
        Guard.Against.Null(assemblyName);

        var simpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(simpleName))
            return false;

        foreach (var selector in selectors)
        {
            if (!selector.TryMatch(simpleName, out var match) || match is null)
                continue;

            if (matchedAssemblyNames.Add(simpleName))
                matches.Add(match);

            return true;
        }

        return false;
    }
}
