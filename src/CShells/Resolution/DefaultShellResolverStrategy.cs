using CShells.Hosting;
using CShells.Management;

namespace CShells.Resolution;

/// <summary>
/// A fallback strategy that prefers the explicit <c>Default</c> shell when it is configured and applied,
/// and otherwise falls back only to applied runtimes.
/// </summary>
[ResolverOrder(1000)]
public class DefaultShellResolverStrategy(IShellHost shellHost, IShellRuntimeStateAccessor runtimeStateAccessor) : IShellResolverStrategy
{
    private readonly IShellHost shellHost = Guard.Against.Null(shellHost);
    private readonly IShellRuntimeStateAccessor runtimeStateAccessor = Guard.Against.Null(runtimeStateAccessor);

    /// <inheritdoc />
    public ShellId? Resolve(ShellResolutionContext context)
    {
        Guard.Against.Null(context);

        var defaultId = new ShellId(ShellConstants.DefaultShellName);
        var explicitDefault = runtimeStateAccessor.GetShell(defaultId);

        if (explicitDefault is not null)
            return explicitDefault.IsRoutable ? defaultId : (ShellId?)null;

        var fallback = shellHost.AllShells.FirstOrDefault();
        if (fallback is null)
            return null;

        return fallback.Id;
    }
}
