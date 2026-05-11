namespace CShells.Features;

internal sealed class SharedAssemblySelector : ISharedAssemblySelector
{
    private readonly SharedAssemblyPattern? pattern;
    private readonly Func<string, bool>? predicate;

    private SharedAssemblySelector(SharedAssemblyPattern? pattern, Func<string, bool>? predicate, string source)
    {
        this.pattern = pattern;
        this.predicate = predicate;
        Source = Guard.Against.NullOrWhiteSpace(source);
    }

    public string Source { get; }

    public static SharedAssemblySelector FromPattern(string pattern, string source) =>
        new(SharedAssemblyPattern.Parse(pattern, source), null, source);

    public static SharedAssemblySelector FromPredicate(Func<string, bool> predicate, string source) =>
        new(null, Guard.Against.Null(predicate), source);

    public bool TryMatch(string assemblyName, out SharedAssemblyMatch? match)
    {
        Guard.Against.Null(assemblyName);

        if (pattern is not null)
        {
            if (!pattern.IsMatch(assemblyName))
            {
                match = null;
                return false;
            }

            match = new(assemblyName, pattern.Kind, pattern.Pattern, Source);
            return true;
        }

        try
        {
            if (predicate?.Invoke(assemblyName) is not true)
            {
                match = null;
                return false;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The shared assembly selector from '{Source}' threw while evaluating assembly simple name '{assemblyName}'. Ensure predicate selectors can evaluate every candidate assembly.", ex);
        }

        match = new(assemblyName, SharedAssemblySelectorKind.Predicate, null, Source);
        return true;
    }
}
