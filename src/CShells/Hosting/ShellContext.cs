namespace CShells.Hosting;

/// <summary>
/// Represents an initialized shell with its settings and service provider.
/// </summary>
public class ShellContext(ShellSettings settings, IServiceProvider serviceProvider, IReadOnlyList<string> enabledFeatures, IReadOnlyCollection<string>? missingFeatures = null)
{
    private int _activeScopes;
    private volatile bool _pendingDisposal;

    /// <summary>
    /// Gets the shell settings.
    /// </summary>
    public ShellSettings Settings { get; } = Guard.Against.Null(settings);

    /// <summary>
    /// Gets the service provider for this shell.
    /// </summary>
    public IServiceProvider ServiceProvider { get; } = Guard.Against.Null(serviceProvider);

    /// <summary>
    /// Gets the shell identifier.
    /// </summary>
    public ShellId Id => Settings.Id;

    /// <summary>
    /// Gets the list of enabled features for this shell, including resolved dependencies.
    /// </summary>
    /// <remarks>
    /// This list contains all features that are active for this shell, including:
    /// <list type="bullet">
    ///   <item><description>Features explicitly listed in <see cref="ShellSettings.EnabledFeatures"/></description></item>
    ///   <item><description>Features transitively required as dependencies</description></item>
    /// </list>
    /// Features are ordered by their dependency requirements (dependencies before dependents).
    /// </remarks>
    public IReadOnlyList<string> EnabledFeatures { get; } = Guard.Against.Null(enabledFeatures);

    /// <summary>
    /// Gets the list of configured feature IDs that were not present in the runtime feature catalog
    /// when this shell was built. Empty when all configured features were available.
    /// </summary>
    public IReadOnlyCollection<string> MissingFeatures { get; } = missingFeatures ?? [];

    /// <summary>
    /// Gets the number of active request scopes currently using this shell context.
    /// </summary>
    internal int ActiveScopes => Volatile.Read(ref _activeScopes);

    /// <summary>
    /// Gets whether this context has been marked for deferred disposal once all active scopes release.
    /// </summary>
    internal bool IsPendingDisposal => _pendingDisposal;

    internal void IncrementActiveScopes() => Interlocked.Increment(ref _activeScopes);

    /// <summary>
    /// Decrements the active scope counter and returns the new count.
    /// </summary>
    internal int DecrementActiveScopes() => Interlocked.Decrement(ref _activeScopes);

    /// <summary>
    /// Marks this context for deferred disposal. The actual disposal will happen when
    /// <see cref="ActiveScopes"/> reaches zero.
    /// </summary>
    internal void MarkPendingDisposal() => _pendingDisposal = true;
}
