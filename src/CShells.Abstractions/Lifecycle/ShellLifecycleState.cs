namespace CShells.Lifecycle;

/// <summary>
/// The monotonic lifecycle states of a single shell generation.
/// </summary>
/// <remarks>
/// Transitions may only move forward through this enum. Backward attempts are no-ops.
/// The only path that bypasses <see cref="Drained"/> is the registry's emergency-dispose
/// on host shutdown-timeout breach.
/// </remarks>
public enum ShellLifecycleState
{
    /// <summary>The shell has been constructed and its initializers are running.</summary>
    Initializing,

    /// <summary>The shell is the currently-active generation; its service provider is serving work.</summary>
    Active,

    /// <summary>A newer generation has been promoted; this generation is transitioning to drain.</summary>
    Deactivating,

    /// <summary>Scope-wait and drain handlers are running; the service provider is still available.</summary>
    Draining,

    /// <summary>Drain has completed (or timed out / been forced); the provider is still valid until disposal.</summary>
    Drained,

    /// <summary>The shell's service provider has been disposed. Terminal.</summary>
    Disposed,
}
