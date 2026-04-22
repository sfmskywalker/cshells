using CShells.DependencyInjection;

namespace CShells.Hosting;

/// <summary>
/// Default implementation of <see cref="IShellServiceExclusionProvider"/> listing core CShells
/// infrastructure types that should not be copied to shell service collections.
/// </summary>
/// <remarks>
/// <see cref="CShells.Lifecycle.IShellRegistry"/> is intentionally <b>not</b> excluded — the
/// root singleton descriptor is copied into each shell so shell-scoped code (e.g., endpoints
/// mapped at the host level, or features that want to trigger reloads) can resolve the
/// registry. Copying a singleton factory descriptor aliases back to the same root instance.
/// </remarks>
public class DefaultShellServiceExclusionProvider : IShellServiceExclusionProvider
{
    /// <inheritdoc />
    public IEnumerable<Type> GetExcludedServiceTypes()
    {
        yield return typeof(IRootServiceCollectionAccessor);
        yield return typeof(IReadOnlyCollection<ShellSettings>);
    }
}
