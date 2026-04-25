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

        // Deterministic fallback: pick the active shell whose name sorts first under
        // case-insensitive ordinal order. Without a stable sort, routing behaviour could drift
        // across process restarts simply because the dictionary's bucket order changed.
        var firstActive = _registry.GetActiveShells()
            .OrderBy(s => s.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return firstActive is null ? (ShellId?)null : new ShellId(firstActive.Descriptor.Name);
    }
}
