namespace CShells.Resolution;

/// <summary>
/// A fallback strategy that always resolves to a shell with Id "Default".
/// This strategy is typically registered last to ensure there's always a shell resolved.
/// </summary>
public class DefaultShellResolverStrategy : IShellResolverStrategy
{
    /// <inheritdoc />
    public ShellId? Resolve(ShellResolutionContext context) => new ShellId("Default");
}
