namespace CShells.Lifecycle;

/// <summary>
/// The authoritative registry for named, generation-stamped shells.
/// </summary>
/// <remarks>
/// One <see cref="IShellBlueprint"/> is registered per shell name. Each call to
/// <see cref="ActivateAsync"/> or <see cref="ReloadAsync"/> re-invokes the blueprint to
/// produce a fresh <see cref="ShellSettings"/>, builds a shell stamped with the next
/// monotonic generation number, runs its <see cref="IShellInitializer"/> services, promotes
/// it to <see cref="ShellLifecycleState.Active"/>, and initiates cooperative drain on the
/// previously-active generation (if any). Multiple generations for the same name may coexist:
/// exactly one is <see cref="ShellLifecycleState.Active"/>, any number may be
/// <see cref="ShellLifecycleState.Deactivating"/> / <see cref="ShellLifecycleState.Draining"/>
/// / <see cref="ShellLifecycleState.Drained"/>.
/// </remarks>
public interface IShellRegistry
{
    /// <summary>Registers a blueprint for a shell name.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a blueprint for <c>blueprint.Name</c> is already registered.
    /// </exception>
    void RegisterBlueprint(IShellBlueprint blueprint);

    /// <summary>Returns the blueprint registered for <paramref name="name"/>, or <c>null</c> if none.</summary>
    IShellBlueprint? GetBlueprint(string name);

    /// <summary>Returns every registered blueprint name.</summary>
    IReadOnlyCollection<string> GetBlueprintNames();

    /// <summary>
    /// Composes fresh settings from the registered blueprint, builds generation 1, runs its
    /// <see cref="IShellInitializer"/> services, and promotes it to
    /// <see cref="ShellLifecycleState.Active"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no blueprint is registered for <paramref name="name"/>, or when a shell
    /// for <paramref name="name"/> is already active (callers should use
    /// <see cref="ReloadAsync"/> to roll over).
    /// </exception>
    Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes fresh settings from the registered blueprint, builds the next generation,
    /// runs its initializers, promotes it to <see cref="ShellLifecycleState.Active"/>, and
    /// initiates cooperative drain on the previously-active generation.
    /// </summary>
    /// <remarks>
    /// If no generation is currently active, behaves equivalently to <see cref="ActivateAsync"/>.
    /// Concurrent calls for the same name are serialized.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no blueprint is registered for <paramref name="name"/>.
    /// </exception>
    Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads every registered blueprint. Independent names reload in parallel; per-name
    /// outcomes are returned so a single failure does not abort the batch.
    /// </summary>
    Task<IReadOnlyList<ReloadResult>> ReloadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a cooperative drain on <paramref name="shell"/>. Drain runs three phases:
    /// (1) wait for all active <see cref="IShellScope"/> handles to release, (2) invoke all
    /// registered <see cref="IDrainHandler"/> services in parallel, (3) grace after deadline
    /// or force. Concurrent calls for the same shell return the same in-flight operation.
    /// </summary>
    Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default);

    /// <summary>The single <see cref="ShellLifecycleState.Active"/> shell for <paramref name="name"/>, or <c>null</c> if none.</summary>
    IShell? GetActive(string name);

    /// <summary>All generations currently held for <paramref name="name"/> regardless of lifecycle state.</summary>
    IReadOnlyCollection<IShell> GetAll(string name);

    /// <summary>Registers a lifecycle subscriber notified of every state transition.</summary>
    void Subscribe(IShellLifecycleSubscriber subscriber);

    /// <summary>Removes a previously-registered lifecycle subscriber.</summary>
    void Unsubscribe(IShellLifecycleSubscriber subscriber);
}
