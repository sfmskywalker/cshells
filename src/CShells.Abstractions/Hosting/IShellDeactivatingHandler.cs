namespace CShells.Hosting;

/// <summary>
/// Handles shell deactivation. Invoked before a shell is removed or during application shutdown.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides a hook for shell-scoped services to perform cleanup tasks
/// before the shell's <see cref="IServiceProvider"/> is disposed.
/// </para>
/// <para>
/// Handlers are invoked in descending <see cref="ShellHandlerOrderAttribute"/> order (highest first),
/// which is the natural inverse of activation order. Handlers with no attribute are treated as order <c>0</c>.
/// </para>
/// <list type="bullet">
///   <item><description>A shell is being dynamically removed via <see cref="IShellManager.RemoveShellAsync"/></description></item>
///   <item><description>The application is shutting down and all shells are being deactivated</description></item>
/// </list>
/// <para>
/// Handlers are invoked BEFORE the shell is removed from the cache and BEFORE its service provider is disposed,
/// ensuring that shell-scoped services are still accessible during cleanup.
/// </para>
/// <para>
/// Register implementations in a shell feature's <see cref="Features.IShellFeature.ConfigureServices"/> method:
/// </para>
/// <code>
/// services.AddSingleton&lt;IShellDeactivatingHandler, MyDeactivationHandler&gt;();
/// </code>
/// <para>
/// Note: Exceptions thrown during deactivation are logged but do not prevent other handlers from running.
/// </para>
/// </remarks>
public interface IShellDeactivatingHandler
{
    /// <summary>
    /// Invoked when the shell is being deactivated, before its service provider is disposed.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous deactivation operation.</returns>
    Task OnDeactivatingAsync(CancellationToken cancellationToken = default);
}
