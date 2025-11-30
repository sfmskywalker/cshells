namespace CShells.Resolution;

/// <summary>
/// Defines a strategy for resolving shell identifiers from a resolution context.
/// Multiple strategies can be registered and will be evaluated in order by <see cref="IShellResolver"/>.
/// </summary>
public interface IShellResolverStrategy
{
    /// <summary>
    /// Attempts to resolve a shell identifier from the provided context.
    /// </summary>
    /// <param name="context">The resolution context containing data for shell resolution.</param>
    /// <returns>A <see cref="ShellId"/> if resolution is successful; otherwise, <c>null</c>.</returns>
    ShellId? Resolve(ShellResolutionContext context);
}
