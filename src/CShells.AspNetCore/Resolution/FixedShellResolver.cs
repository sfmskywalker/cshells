using CShells.Resolution;

namespace CShells.AspNetCore.Resolution;

/// <summary>
/// A resolver strategy that always returns a fixed shell identifier.
/// </summary>
public class FixedShellResolver(ShellId shellId) : IShellResolverStrategy
{
    public Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<ShellId?>(shellId);
}
