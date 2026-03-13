using CShells.DependencyInjection;
using CShells.Management;

namespace CShells.Hosting;

/// <summary>
/// Default implementation of <see cref="IShellServiceExclusionProvider"/> that provides
/// core CShells infrastructure types that should not be copied to shell service collections.
/// </summary>
public class DefaultShellServiceExclusionProvider : IShellServiceExclusionProvider
{
    /// <inheritdoc />
    public IEnumerable<Type> GetExcludedServiceTypes()
    {
        yield return typeof(IShellHost);
        yield return typeof(IShellContextScopeFactory);
        yield return typeof(IRootServiceCollectionAccessor);
        yield return typeof(IReadOnlyCollection<ShellSettings>);
        // IShellManager depends on IShellHost (which is excluded above), so copying the root
        // descriptor would produce a broken registration in shell containers.
        // It is re-registered in each shell as a root-provider delegation (see RegisterCoreServices).
        yield return typeof(IShellManager);
    }
}
