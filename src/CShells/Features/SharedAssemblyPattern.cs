namespace CShells.Features;

internal sealed class SharedAssemblyPattern
{
    private readonly string value;
    private readonly StringComparison comparison;

    private SharedAssemblyPattern(string value, SharedAssemblySelectorKind kind)
    {
        this.value = value;
        Kind = kind;
        comparison = StringComparison.OrdinalIgnoreCase;
    }

    public SharedAssemblySelectorKind Kind { get; }

    public string Pattern => Kind is SharedAssemblySelectorKind.PrefixPattern
        ? $"{value}*"
        : value;

    public static SharedAssemblyPattern Parse(string? pattern, string source)
    {
        Guard.Against.NullOrWhiteSpace(source);

        if (pattern is null)
            throw new ArgumentNullException(nameof(pattern), $"Shared assembly selector from '{source}' cannot be null.");

        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException($"Shared assembly selector from '{source}' cannot be blank or whitespace-only.", nameof(pattern));

        var trimmed = pattern.Trim();
        var wildcardIndex = trimmed.IndexOf('*', StringComparison.Ordinal);
        if (wildcardIndex < 0)
            return new(trimmed, SharedAssemblySelectorKind.Exact);

        if (wildcardIndex != trimmed.Length - 1)
            throw new ArgumentException($"Shared assembly selector '{trimmed}' from '{source}' is invalid. The '*' wildcard is allowed only as the final character.", nameof(pattern));

        if (trimmed.Length == 1)
            throw new ArgumentException($"Shared assembly selector '{trimmed}' from '{source}' is invalid. Prefix wildcard patterns must include a non-empty prefix before '*'.", nameof(pattern));

        return new(trimmed[..^1], SharedAssemblySelectorKind.PrefixPattern);
    }

    public bool IsMatch(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return false;

        return Kind is SharedAssemblySelectorKind.Exact
            ? string.Equals(assemblyName, value, comparison)
            : assemblyName.StartsWith(value, comparison);
    }
}
