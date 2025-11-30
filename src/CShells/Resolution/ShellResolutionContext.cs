namespace CShells.Resolution;

/// <summary>
/// Provides a protocol-agnostic context for resolving shell identifiers.
/// </summary>
public class ShellResolutionContext
{
    /// <summary>
    /// Gets the generic key-value storage for context data.
    /// </summary>
    public IDictionary<string, object> Data { get; init; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a strongly-typed value from the context data.
    /// </summary>
    /// <typeparam name="T">The expected type of the value.</typeparam>
    /// <param name="key">The key to retrieve.</param>
    /// <returns>The value if found and of the expected type; otherwise, default.</returns>
    public T? Get<T>(string key) => Data.TryGetValue(key, out var value) && value is T typed ? typed : default;

    /// <summary>
    /// Sets a strongly-typed value in the context data.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to set.</param>
    public void Set<T>(string key, T value) where T : notnull => Data[key] = value;
}
