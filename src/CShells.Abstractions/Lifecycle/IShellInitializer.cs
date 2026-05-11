using Microsoft.Extensions.DependencyInjection;

namespace CShells.Lifecycle;

/// <summary>
/// Performs one-time setup work for a shell as part of its
/// <see cref="ShellLifecycleState.Initializing"/> → <see cref="ShellLifecycleState.Active"/>
/// transition.
/// </summary>
/// <remarks>
/// Register implementations as transient services via <c>IShellFeature.ConfigureServices</c>
/// on the shell's <see cref="IServiceCollection"/>. Existing unordered registrations run
/// in <see cref="LifecyclePhase.Default"/> and preserve DI-registration order relative to
/// one another. Features that need explicit activation order should use
/// <see cref="ServiceCollectionLifecycleExtensions.AddShellInitializer{TInitializer}(IServiceCollection, LifecyclePhase, int)"/>
/// or <see cref="LifecycleOrderAttribute"/> instead of manually replacing or reordering
/// service descriptors. The registry resolves initializers from the newly-built shell
/// provider and awaits each initializer sequentially before promoting the shell to
/// <see cref="ShellLifecycleState.Active"/>. An initializer that throws aborts activation.
/// </remarks>
public interface IShellInitializer
{
    /// <summary>Performs initialization work.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
