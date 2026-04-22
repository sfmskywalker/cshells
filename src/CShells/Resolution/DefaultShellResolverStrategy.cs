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

        foreach (var name in _registry.GetBlueprintNames())
        {
            var shell = _registry.GetActive(name);
            if (shell is not null)
                return new ShellId(shell.Descriptor.Name);
        }

        return null;
    }
}
