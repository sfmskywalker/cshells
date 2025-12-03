using CShells.Resolution;

namespace CShells.AspNetCore.Resolution;

/// <summary>
/// A resolver strategy that always returns a fixed shell identifier.
/// </summary>
public class FixedShellResolver(ShellId shellId) : IShellResolverStrategy
{
    private readonly ShellId _shellId = shellId;
    public ShellId? Resolve(ShellResolutionContext context) => _shellId;
}
