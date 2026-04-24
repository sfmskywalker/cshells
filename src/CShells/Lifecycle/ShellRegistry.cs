using System.Collections.Concurrent;
using System.Collections.Immutable;
using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Lifecycle;

/// <summary>
/// Default <see cref="IShellRegistry"/> implementation. Holds the in-memory index of
/// <b>active</b> shell generations; delegates blueprint lookup and catalogue listing to the
/// injected <see cref="CompositeShellBlueprintProvider"/>.
/// </summary>
internal sealed class ShellRegistry : IShellRegistry
{
    private readonly ShellProviderBuilder? _providerBuilder;
    private readonly IServiceProvider? _rootProvider;
    private readonly CompositeShellBlueprintProvider _blueprintProvider;
    private readonly ILogger<ShellRegistry> _logger;
    private readonly ConcurrentDictionary<string, NameSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    // Lazy wrapper: `ConcurrentDictionary.GetOrAdd(key, factory)` does NOT guarantee the factory
    // runs at most once under contention — concurrent callers can both enter the factory even
    // though only one result is stored. Wrapping in Lazy<T> guarantees `StartDrain` (and
    // therefore `op.RunAsync()`) fires exactly once per shell.
    private readonly ConcurrentDictionary<IShell, Lazy<DrainOperation>> _drainOps = new();
    private ImmutableList<IShellLifecycleSubscriber> _subscribers = [];

    public ShellRegistry(
        CompositeShellBlueprintProvider blueprintProvider,
        ShellProviderBuilder? providerBuilder = null,
        IServiceProvider? rootProvider = null,
        ILogger<ShellRegistry>? logger = null,
        IEnumerable<IShellLifecycleSubscriber>? subscribers = null)
    {
        _blueprintProvider = Guard.Against.Null(blueprintProvider);
        _providerBuilder = providerBuilder;
        _rootProvider = rootProvider;
        _logger = logger ?? NullLogger<ShellRegistry>.Instance;

        // Subscribers registered in DI are subscribed at construction time so they observe
        // every transition — including the first activation kicked off by the startup hosted
        // service. Without this, factory-based registrations (e.g., AddSingleton<…>(sp => …))
        // would only materialize on the first GetServices<IShellLifecycleSubscriber>() call,
        // which never happens in the normal flow.
        if (subscribers is not null)
        {
            foreach (var subscriber in subscribers)
                Subscribe(subscriber);
        }
    }

    // Convenience ctor used by tests that don't need the provider-build pipeline.
    internal ShellRegistry(CompositeShellBlueprintProvider blueprintProvider, ILogger<ShellRegistry>? logger)
        : this(blueprintProvider, providerBuilder: null, rootProvider: null, logger, subscribers: null)
    {
    }

    // =========================================================================
    // Activation
    // =========================================================================

    /// <inheritdoc />
    public async Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        EnsureProviderBuilder();

        var slot = _slots.GetOrAdd(name, static _ => new NameSlot());

        // Fast path: active shell already published. Volatile read via field.
        if (slot.Active is { } existing)
            return existing;

        await slot.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check under the semaphore: a concurrent caller may have activated in the
            // meantime. This is the stampede-safety guarantee — exactly one build per name.
            if (slot.Active is { } alreadyActive)
                return alreadyActive;

            var blueprint = await LookupBlueprintAsync(name, wrapFault: true, cancellationToken).ConfigureAwait(false)
                ?? throw new ShellBlueprintNotFoundException(name);

            return await CreateGenerationAsync(slot, blueprint.Blueprint, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.Semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        EnsureProviderBuilder();

        var slot = _slots.GetOrAdd(name, static _ => new NameSlot());

        await slot.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Active is not null)
                throw new InvalidOperationException(
                    $"Shell '{name}' already has an Active generation (generation {slot.Active.Descriptor.Generation}). Use ReloadAsync to roll over or GetOrActivateAsync for idempotent access.");

            var blueprint = await LookupBlueprintAsync(name, wrapFault: true, cancellationToken).ConfigureAwait(false)
                ?? throw new ShellBlueprintNotFoundException(name);

            return await CreateGenerationAsync(slot, blueprint.Blueprint, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.Semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        EnsureProviderBuilder();

        var slot = _slots.GetOrAdd(name, static _ => new NameSlot());

        Shell? previousActive = null;
        IShell? newShell = null;
        Exception? error = null;

        // Not-found is a caller-programming error and propagates eagerly — callers expect to
        // know immediately that a reload target is unknown. Composition/build/initializer
        // failures are captured into ReloadResult.Error so ReloadActiveAsync can continue the
        // batch past a transient single-shell fault.
        var provided = await LookupBlueprintAsync(name, wrapFault: true, cancellationToken).ConfigureAwait(false)
            ?? throw new ShellBlueprintNotFoundException(name);

        await slot.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            previousActive = slot.Active;

            try
            {
                newShell = await CreateGenerationAsync(slot, provided.Blueprint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Current active generation is unaffected; no partial entry retained.
                error = ex;
            }

            if (newShell is not null && previousActive is not null)
            {
                // Promote the new generation by transitioning the old one to Deactivating. Drain
                // runs outside the semaphore so a slow drain doesn't block the next reload.
                await previousActive.ForceAdvanceAsync(ShellLifecycleState.Deactivating).ConfigureAwait(false);
            }
        }
        finally
        {
            slot.Semaphore.Release();
        }

        if (error is not null)
            return new ReloadResult(name, NewShell: null, Drain: null, Error: error);

        IDrainOperation? drainOp = null;
        if (previousActive is not null)
            drainOp = await DrainAsync(previousActive, cancellationToken).ConfigureAwait(false);

        return new ReloadResult(name, NewShell: newShell, Drain: drainOp, Error: null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(
        ReloadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new ReloadOptions();
        opts.EnsureValid();

        var activeNames = _slots
            .Where(kv => kv.Value.Active is not null)
            .Select(kv => kv.Key)
            .ToList();

        if (activeNames.Count == 0)
            return [];

        var results = new ConcurrentBag<ReloadResult>();
        await Parallel.ForEachAsync(
            activeNames,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = opts.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (name, ct) =>
            {
                try
                {
                    results.Add(await ReloadAsync(name, ct).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    results.Add(new ReloadResult(name, NewShell: null, Drain: null, Error: ex));
                }
            }).ConfigureAwait(false);

        return results.ToList();
    }

    // =========================================================================
    // Unregister
    // =========================================================================

    /// <inheritdoc />
    public async Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        // Phase 0: resolve the blueprint + its owning manager via the provider. Raw propagation
        // here (no wrap into ShellBlueprintUnavailableException) — unregister is an admin flow
        // and callers want the original fault for diagnostics.
        var provided = await _blueprintProvider.GetAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new ShellBlueprintNotFoundException(name);

        if (provided.Manager is null)
            throw new BlueprintNotMutableException(name);

        // Phase 1: persist the delete. Propagates manager exceptions raw.
        await provided.Manager.DeleteAsync(name, cancellationToken).ConfigureAwait(false);

        // Phase 2: drain + remove in-memory slot. Serializes against any in-flight activation
        // for this name via the slot's semaphore.
        if (!_slots.TryGetValue(name, out var slot))
            return;  // Nothing active; persistent state was cleaned and nothing else to do.

        Shell? activeToDrain;
        await slot.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            activeToDrain = slot.Active;
            slot.Active = null;
        }
        finally
        {
            slot.Semaphore.Release();
        }

        if (activeToDrain is not null)
        {
            var drainOp = await DrainAsync(activeToDrain, cancellationToken).ConfigureAwait(false);
            await drainOp.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Remove the slot entirely so repeated unregister + re-create cycles don't leave
        // stranded semaphores in the dict. `_slots.TryRemove(name, out _)` is safe even under
        // a concurrent `GetOrActivateAsync` for the same name — the re-create path will
        // allocate a fresh slot.
        _slots.TryRemove(name, out _);
    }

    // =========================================================================
    // Read access
    // =========================================================================

    /// <inheritdoc />
    public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return _blueprintProvider.GetAsync(name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);
        var provided = await _blueprintProvider.GetAsync(name, cancellationToken).ConfigureAwait(false);
        return provided?.Manager;
    }

    /// <inheritdoc />
    public async Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(query);
        query.EnsureValid();

        var catalogue = await _blueprintProvider.ListAsync(
            new BlueprintListQuery(query.Cursor, query.Limit, query.NamePrefix),
            cancellationToken).ConfigureAwait(false);

        var items = catalogue.Items
            .Select(summary => BuildShellSummary(summary))
            .Where(summary => query.StateFilter is null || summary.State == query.StateFilter)
            .ToList();

        return new ShellPage(items, catalogue.NextCursor);
    }

    private ShellSummary BuildShellSummary(BlueprintSummary summary)
    {
        if (!_slots.TryGetValue(summary.Name, out var slot) || slot.Active is not { } active)
        {
            return new ShellSummary(
                summary.Name,
                summary.SourceId,
                summary.Mutable,
                ActiveGeneration: null,
                State: null,
                ActiveScopeCount: 0,
                LastScopeOpenedAt: null,
                summary.Metadata);
        }

        return new ShellSummary(
            summary.Name,
            summary.SourceId,
            summary.Mutable,
            ActiveGeneration: active.Descriptor.Generation,
            State: active.State,
            ActiveScopeCount: active.ActiveScopeCount,
            LastScopeOpenedAt: null,  // populated in feature 008 when LastScopeOpenedAt lands on Shell
            summary.Metadata);
    }

    /// <inheritdoc />
    public IShell? GetActive(string name)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return _slots.TryGetValue(name, out var slot) ? slot.Active : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IShell> GetAll(string name)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return _slots.TryGetValue(name, out var slot) ? slot.All : [];
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IShell> GetActiveShells() =>
        _slots.Values.Select(s => s.Active).OfType<IShell>().ToList();

    // =========================================================================
    // Drain
    // =========================================================================

    /// <inheritdoc />
    public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shell);

        if (shell is not Shell typedShell)
            throw new ArgumentException(
                $"DrainAsync only accepts shells produced by this registry (CShells.Lifecycle.Shell); got {shell.GetType().FullName}.",
                nameof(shell));

        // Idempotent while in flight: concurrent callers during the same drain get the same
        // `DrainOperation` instance. The Lazy wrapper is essential — ConcurrentDictionary.GetOrAdd
        // may invoke the factory more than once under contention (only one result wins), which
        // would fire `op.RunAsync()` twice and double-invoke every handler. Once the drain's run
        // task completes the entry is removed (see StartDrain), so calling DrainAsync for an
        // already-drained shell will produce a fresh operation — acceptable because that is a
        // programming error against a disposed provider.
        var lazy = _drainOps.GetOrAdd(shell, s => new Lazy<DrainOperation>(
            () => StartDrain((Shell)s),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return Task.FromResult<IDrainOperation>(lazy.Value);
    }

    private DrainOperation StartDrain(Shell shell)
    {
        var policy = ResolveDrainPolicy();
        var gracePeriod = ResolveGracePeriod();
        var op = new DrainOperation(shell, policy, gracePeriod, ResolveDrainLogger());

        // Transition to Draining (Active or Deactivating → Draining). Best-effort CAS.
        _ = shell.ForceAdvanceAsync(ShellLifecycleState.Draining);

        // Remove the dictionary entry when the run completes (success or fault) so long-running
        // hosts don't accumulate one drained Shell + DrainOperation + TCS + CTS per reload.
        _ = op.RunAsync().ContinueWith(
            runTask => _drainOps.TryRemove(shell, out _),
            TaskContinuationOptions.ExecuteSynchronously);
        return op;
    }

    private IDrainPolicy ResolveDrainPolicy()
    {
        if (_rootProvider is null)
            return new Policies.FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30));

        return _rootProvider.GetService<IDrainPolicy>()
               ?? new Policies.FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30));
    }

    private TimeSpan ResolveGracePeriod() =>
        _rootProvider?.GetService<DrainGracePeriod>()?.Value ?? TimeSpan.FromSeconds(3);

    private ILogger<DrainOperation>? ResolveDrainLogger() =>
        _rootProvider?.GetService<ILogger<DrainOperation>>();

    // =========================================================================
    // Subscribers
    // =========================================================================

    /// <inheritdoc />
    public void Subscribe(IShellLifecycleSubscriber subscriber)
    {
        Guard.Against.Null(subscriber);
        ImmutableInterlocked.Update(ref _subscribers,
            static (list, s) => list.Contains(s) ? list : list.Add(s),
            subscriber);
    }

    /// <inheritdoc />
    public void Unsubscribe(IShellLifecycleSubscriber subscriber)
    {
        Guard.Against.Null(subscriber);
        ImmutableInterlocked.Update(ref _subscribers,
            static (list, s) => list.Remove(s),
            subscriber);
    }

    /// <summary>
    /// Fans out a state-change event to every registered subscriber. Subscriber exceptions are
    /// caught and logged so one failing subscriber cannot block peers or the transition.
    /// </summary>
    internal async Task FireStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _subscribers;
        if (snapshot.IsEmpty)
            return;

        foreach (var subscriber in snapshot)
        {
            try
            {
                await subscriber.OnStateChangedAsync(shell, previous, current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Shell lifecycle subscriber {SubscriberType} threw during {Previous} → {Current} for shell {Shell}",
                    subscriber.GetType().FullName, previous, current, shell.Descriptor);
            }
        }
    }

    // =========================================================================
    // Internals
    // =========================================================================

    /// <summary>
    /// Resolves a blueprint from the composite provider, optionally wrapping provider faults in
    /// <see cref="ShellBlueprintUnavailableException"/>. Activation entry points wrap; the
    /// public <see cref="GetBlueprintAsync"/> and <see cref="UnregisterBlueprintAsync"/> paths
    /// do NOT wrap (they want the raw fault for diagnostics).
    /// </summary>
    private async Task<ProvidedBlueprint?> LookupBlueprintAsync(string name, bool wrapFault, CancellationToken cancellationToken)
    {
        try
        {
            return await _blueprintProvider.GetAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (wrapFault && ex is not ShellBlueprintNotFoundException && ex is not OperationCanceledException)
        {
            throw new ShellBlueprintUnavailableException(name, ex);
        }
    }

    private void EnsureProviderBuilder()
    {
        if (_providerBuilder is null)
            throw new InvalidOperationException(
                "Registry was constructed without a ShellProviderBuilder. Use AddCShells(...) to configure the container.");
    }

    /// <summary>
    /// Compose → build → initialize → promote. Must be called under the name's semaphore.
    /// </summary>
    private async Task<IShell> CreateGenerationAsync(NameSlot slot, IShellBlueprint blueprint, CancellationToken cancellationToken)
    {
        // Assign the generation number. If the rest of this method throws we simply "skip" this
        // number; the next successful reload picks up the following value. This satisfies
        // no-reuse and no-partial-entry without bookkeeping.
        var generation = Interlocked.Increment(ref slot.NextGeneration);

        var settings = await blueprint.ComposeAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(settings.Id.Name, blueprint.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Blueprint '{blueprint.Name}' produced settings with Id.Name '{settings.Id.Name}' — blueprint name mismatch.");

        var buildResult = await _providerBuilder!.BuildAsync(settings, cancellationToken).ConfigureAwait(false);

        var descriptor = ShellDescriptor.Create(blueprint.Name, (int)generation, blueprint.Metadata);
        var shell = new Shell(descriptor, buildResult.Provider, (s, prev, curr) =>
            FireStateChangedAsync(s, prev, curr, cancellationToken: default));

        // Populate the holder so services in the shell's provider can resolve IShell.
        buildResult.Holder.Set(shell);

        try
        {
            await RunInitializersAsync(buildResult.Provider, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposePartialProviderAsync(buildResult.Provider).ConfigureAwait(false);
            throw;
        }

        if (!await shell.TryTransitionAsync(ShellLifecycleState.Initializing, ShellLifecycleState.Active).ConfigureAwait(false))
            throw new InvalidOperationException("Shell failed to transition from Initializing to Active.");

        slot.Active = shell;
        slot.All = slot.All.Add(shell);

        _logger.LogInformation("Activated shell {Descriptor} with {FeatureCount} feature(s); {MissingCount} missing",
            descriptor, buildResult.EnabledFeatures.Count, buildResult.MissingFeatures.Count);

        return shell;
    }

    private static async Task RunInitializersAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        // Initializers run sequentially in DI-registration order.
        await using var scope = provider.CreateAsyncScope();
        var initializers = scope.ServiceProvider.GetServices<IShellInitializer>();
        foreach (var initializer in initializers)
        {
            await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask DisposePartialProviderAsync(ServiceProvider provider)
    {
        try
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Partial-provider disposal failures are swallowed — the primary exception already
            // propagates, and tearing down a half-built container sometimes throws benignly.
        }
    }

    /// <summary>
    /// Per-name state: a serialization semaphore for activate/reload/unregister, a generation
    /// counter, and the currently-active + historical shells. The catalogue blueprint itself
    /// is NOT held here — it lives in the provider and is fetched on every activation.
    /// </summary>
    private sealed class NameSlot
    {
        internal readonly SemaphoreSlim Semaphore = new(1, 1);

        // Incremented under the Semaphore. `long` so the cast to int in ShellDescriptor is explicit.
        internal long NextGeneration;

        // Mutated under the Semaphore; read without locking by GetActive.
        internal volatile Shell? Active;

        // Immutable list; replaced under the Semaphore.
        internal ImmutableList<Shell> All = [];
    }
}
