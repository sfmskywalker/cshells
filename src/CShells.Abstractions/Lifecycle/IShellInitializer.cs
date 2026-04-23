namespace CShells.Lifecycle;

/// <summary>
/// Performs one-time setup work for a shell as part of its
/// <see cref="ShellLifecycleState.Initializing"/> → <see cref="ShellLifecycleState.Active"/>
/// transition.
/// </summary>
/// <remarks>
/// Register implementations as transient services via <c>IShellFeature.ConfigureServices</c>
/// on the shell's <see cref="IServiceCollection"/>. The registry resolves
/// <c>IEnumerable&lt;IShellInitializer&gt;</c> from the newly-built provider and awaits each
/// initializer sequentially in DI-registration order before promoting the shell to
/// <see cref="ShellLifecycleState.Active"/>. An initializer that throws aborts activation.
/// Features MUST NOT use <c>IServiceCollection.Replace</c> on initializer registrations.
/// </remarks>
public interface IShellInitializer
{
    /// <summary>Performs initialization work.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
