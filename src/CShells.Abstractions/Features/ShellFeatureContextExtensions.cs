namespace CShells.Features;

/// <summary>
/// Extension methods for <see cref="ShellFeatureContext.Properties"/>.
/// </summary>
public static class ShellFeatureContextExtensions
{
    /// <summary>
    /// Gets the value for <paramref name="key"/> from <see cref="ShellFeatureContext.Properties"/>,
    /// or adds it using <paramref name="factory"/> if the key does not exist yet.
    /// </summary>
    public static T GetOrAdd<T>(this ShellFeatureContext context, object key, Func<T> factory)
    {
        if (context.Properties.TryGetValue(key, out var existing))
            return (T)existing;

        var value = factory();
        context.Properties[key] = value!;
        return value;
    }
}

