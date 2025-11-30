namespace CShells.Resolution;

/// <summary>
/// High-level service for resolving shell identifiers by orchestrating multiple <see cref="IShellResolverStrategy"/> instances.
/// This is the main resolver service used by the application to determine the current shell.
/// </summary>
public interface IShellResolver
{
    /// <summary>
    /// Resolves a shell identifier from the provided context by trying all registered strategies in order.
    /// </summary>
    /// <param name="context">The resolution context containing data for shell resolution.</param>
    /// <returns>A <see cref="ShellId"/> if any strategy successfully resolves; otherwise, <c>null</c>.</returns>
    ShellId? Resolve(ShellResolutionContext context);
}
