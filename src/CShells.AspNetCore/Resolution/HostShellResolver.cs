using CShells.Resolution;

namespace CShells.AspNetCore.Resolution;

/// <summary>
/// A shell resolver strategy that determines the shell based on the host name.
/// </summary>
public class HostShellResolver : IShellResolverStrategy
{
    private readonly Dictionary<string, ShellId> _hostMap;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostShellResolver"/> class.
    /// </summary>
    /// <param name="hostMap">A dictionary mapping host names to shell identifiers.
    /// Keys should be host names (e.g., "tenant1.example.com", "localhost"). Matching is case-insensitive.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hostMap"/> is null.</exception>
    public HostShellResolver(IReadOnlyDictionary<string, ShellId> hostMap)
    {
        ArgumentNullException.ThrowIfNull(hostMap);
        _hostMap = new(hostMap, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ShellId? Resolve(ShellResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var host = context.Get<string>(ShellResolutionContextKeys.Host);
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        if (_hostMap.TryGetValue(host, out var shellId))
        {
            return shellId;
        }

        return null;
    }
}
