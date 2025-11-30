using CShells.Resolution;

namespace CShells.AspNetCore.Resolution;

/// <summary>
/// A resolver that always returns a fixed shell identifier.
/// </summary>
public class FixedShellResolver : IShellResolver
{
    private readonly ShellId _shellId;

    public FixedShellResolver(ShellId shellId)
    {
        _shellId = shellId;
    }

    public ShellId? Resolve(ShellResolutionContext context) => _shellId;
}
