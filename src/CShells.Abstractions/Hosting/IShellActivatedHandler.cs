namespace CShells.Hosting;

/// <summary>
/// Handles shell activation. Invoked when a shell's service provider is ready and the shell has been fully initialized.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides a hook for shell-scoped services to perform initialization tasks
/// after the shell's <see cref="IServiceProvider"/> has been built and is ready for use.
/// </para>
/// <para>
/// Handlers are invoked in registration order when:
/// </para>
/// <list type="bullet">
///   <item><description>The application starts and all configured shells are loaded</description></item>
///   <item><description>A new shell is dynamically added via <see cref="IShellManager.AddShellAsync"/></description></item>
/// </list>
/// <para>
/// Register implementations in a shell feature's <see cref="Features.IShellFeature.ConfigureServices"/> method:
/// </para>
/// <code>
/// services.AddSingleton&lt;IShellActivatedHandler, MyActivationHandler&gt;();
/// </code>
/// </remarks>
public interface IShellActivatedHandler
{
    /// <summary>
    /// Invoked when the shell has been activated and its service provider is ready.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous activation operation.</returns>
    Task OnActivatedAsync(CancellationToken cancellationToken = default);
}
