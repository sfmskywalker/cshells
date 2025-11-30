namespace CShells.Resolution;

/// <summary>
/// Defines a contract for resolving shell identifiers from a resolution context.
/// </summary>
public interface IShellResolver
{
    /// <summary>
    /// Resolves a shell identifier from the provided context.
    /// </summary>
    /// <param name="context">The resolution context containing data for shell resolution.</param>
    /// <returns>A <see cref="ShellId"/> if resolution is successful; otherwise, <c>null</c>.</returns>
    ShellId? Resolve(ShellResolutionContext context);
}
