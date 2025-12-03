namespace CShells.Resolution;

/// <summary>
/// Default implementation of <see cref="IShellResolver"/> that orchestrates multiple <see cref="IShellResolverStrategy"/> instances.
/// </summary>
public class DefaultShellResolver(IEnumerable<IShellResolverStrategy> strategies, ShellResolverOptions? options = null) : IShellResolver
{
    private const int DefaultOrder = 100;

    private readonly IShellResolverStrategy[] _orderedStrategies = (strategies ?? throw new ArgumentNullException(nameof(strategies)))
        .OrderBy(s => GetOrderForStrategy(s, options))
        .ToArray();

    private static int GetOrderForStrategy(IShellResolverStrategy strategy, ShellResolverOptions? options)
    {
        var strategyType = strategy.GetType();

        var configuredOrder = options?.GetOrder(strategyType);
        if (configuredOrder.HasValue)
            return configuredOrder.Value;

        var attribute = strategyType.GetCustomAttributes(typeof(ResolverOrderAttribute), inherit: true)
            .OfType<ResolverOrderAttribute>()
            .FirstOrDefault();

        return attribute?.Order ?? DefaultOrder;
    }

    /// <inheritdoc />
    public ShellId? Resolve(ShellResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var strategy in _orderedStrategies)
        {
            var shellId = strategy.Resolve(context);
            if (shellId.HasValue)
                return shellId;
        }

        return null;
    }
}
