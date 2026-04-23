namespace CShells.Resolution;

/// <summary>
/// Protocol-agnostic context carrying the request data resolvers use to identify the target
/// shell. Resolvers inject <see cref="CShells.Lifecycle.IShellRegistry"/> directly for shell
/// lookup.
/// </summary>
public class ShellResolutionContext
{
    public IDictionary<string, object> Data { get; init; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    public T? Get<T>(string key) => Data.TryGetValue(key, out var value) && value is T typed ? typed : default;

    public void Set<T>(string key, T value) where T : notnull => Data[key] = value;
}
