namespace CShells.Resolution;

/// <summary>
/// Default implementation of <see cref="IShellResolver"/> that orchestrates multiple <see cref="IShellResolverStrategy"/> instances.
/// Tries each strategy in order and returns the first non-null result.
/// </summary>
public class DefaultShellResolver : IShellResolver
{
    private readonly IEnumerable<IShellResolverStrategy> _strategies;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultShellResolver"/> class.
    /// </summary>
    /// <param name="strategies">The collection of strategies to evaluate in order.</param>
    public DefaultShellResolver(IEnumerable<IShellResolverStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies;
    }

    /// <inheritdoc />
    public ShellId? Resolve(ShellResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var strategy in _strategies)
        {
            var shellId = strategy.Resolve(context);
            if (shellId.HasValue)
            {
                return shellId;
            }
        }

        return null;
    }
}
