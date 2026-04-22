using CShells.Lifecycle;

namespace CShells.Resolution;

/// <summary>
/// Fallback resolver strategy: prefers the explicit <c>Default</c> shell when it is
/// currently <see cref="ShellLifecycleState.Active"/>; otherwise returns the first active
/// shell in the registry.
/// </summary>
[ResolverOrder(1000)]
public class DefaultShellResolverStrategy(IShellRegistry registry) : IShellResolverStrategy
{
    private readonly IShellRegistry _registry = Guard.Against.Null(registry);

    /// <inheritdoc />
    public ShellId? Resolve(ShellResolutionContext context)
    {
        Guard.Against.Null(context);

        var defaultShell = _registry.GetActive(ShellConstants.DefaultShellName);
        if (defaultShell is not null)
            return new ShellId(defaultShell.Descriptor.Name);

        // Deterministic fallback: `GetBlueprintNames` makes no ordering guarantee (it's a
        // ConcurrentDictionary projection), so pick the first active shell in a stable,
        // culture-invariant, case-insensitive order. Without this, routing behaviour could
        // drift across process restarts simply because the dictionary's bucket order changed.
        foreach (var name in _registry.GetBlueprintNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var shell = _registry.GetActive(name);
            if (shell is not null)
                return new ShellId(shell.Descriptor.Name);
        }

        return null;
    }
}
