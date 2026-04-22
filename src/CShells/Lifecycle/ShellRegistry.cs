using System.Collections.Concurrent;
using System.Collections.Immutable;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Lifecycle;

/// <summary>
/// Default <see cref="IShellRegistry"/> implementation.
/// </summary>
internal sealed class ShellRegistry : IShellRegistry
{
    private readonly ShellProviderBuilder? _providerBuilder;
    private readonly IServiceProvider? _rootProvider;
    private readonly ILogger<ShellRegistry> _logger;
    private readonly ConcurrentDictionary<string, NameSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<IShell, DrainOperation> _drainOps = new();
    private ImmutableList<IShellLifecycleSubscriber> _subscribers = [];

    public ShellRegistry(
        IEnumerable<IShellBlueprint>? blueprints = null,
        ShellProviderBuilder? providerBuilder = null,
        IServiceProvider? rootProvider = null,
        ILogger<ShellRegistry>? logger = null)
    {
        _providerBuilder = providerBuilder;
        _rootProvider = rootProvider;
        _logger = logger ?? NullLogger<ShellRegistry>.Instance;

        if (blueprints is null)
            return;

        foreach (var blueprint in blueprints)
            RegisterBlueprint(blueprint);
    }

    // Convenience ctor used by tests that don't need the provider-build pipeline.
    internal ShellRegistry(ILogger<ShellRegistry>? logger)
        : this(blueprints: null, providerBuilder: null, rootProvider: null, logger)
    {
    }

    /// <inheritdoc />
    public void RegisterBlueprint(IShellBlueprint blueprint)
    {
        Guard.Against.Null(blueprint);
        Guard.Against.NullOrWhiteSpace(blueprint.Name, nameof(blueprint) + "." + nameof(blueprint.Name));

        var slot = _slots.GetOrAdd(blueprint.Name, static _ => new NameSlot());
        if (Interlocked.CompareExchange(ref slot.Blueprint, blueprint, null) is not null)
        {
            throw new InvalidOperationException(
                $"A blueprint is already registered for shell '{blueprint.Name}'. Duplicate registration is a programming error.");
        }
    }

    /// <inheritdoc />
    public IShellBlueprint? GetBlueprint(string name)
    {
        Guard.Against.NullOrWhiteSpace(name);
        return _slots.TryGetValue(name, out var slot) ? slot.Blueprint : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetBlueprintNames() =>
        _slots.Where(kv => kv.Value.Blueprint is not null).Select(kv => kv.Key).ToList();

    /// <inheritdoc />
    public async Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        if (_providerBuilder is null)
            throw new InvalidOperationException("Registry was constructed without a ShellProviderBuilder. Use AddCShells(...) to configure the container.");

        var slot = _slots.TryGetValue(name, out var found) ? found : null;
        var blueprint = slot?.Blueprint
            ?? throw new InvalidOperationException($"No blueprint registered for shell '{name}'.");

        await slot!.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Active is not null)
                throw new InvalidOperationException(
                    $"Shell '{name}' already has an Active generation (generation {slot.Active.Descriptor.Generation}). Use ReloadAsync to roll over.");

            return await CreateGenerationAsync(slot, blueprint, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.Semaphore.Release();
        }
    }

    /// <summary>
    /// Compose → build → initialize → promote. Must be called under the name's semaphore.
    /// </summary>
    private async Task<IShell> CreateGenerationAsync(NameSlot slot, IShellBlueprint blueprint, CancellationToken cancellationToken)
    {
        // Assign the generation number. If the rest of this method throws we simply "skip" this
        // number; the next successful reload picks up the following value. This satisfies FR-005
        // (no reuse) and FR-014 (no partial entry) without bookkeeping.
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

        // Fire the synthetic "null → Initializing" transition (represented internally as the
        // Initializing → Initializing no-op; the state machine starts in Initializing so we just
        // notify subscribers of creation by raising Initializing → Initializing).
        // We skip this; the first observable transition is Initializing → Active after initializers.

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
        // Initializers run sequentially in DI-registration order (FR-016).
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

    /// <inheritdoc />
    public async Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name);

        if (_providerBuilder is null)
            throw new InvalidOperationException("Registry was constructed without a ShellProviderBuilder. Use AddCShells(...) to configure the container.");

        var slot = _slots.TryGetValue(name, out var found) ? found : null;
        var blueprint = slot?.Blueprint
            ?? throw new InvalidOperationException($"No blueprint registered for shell '{name}'.");

        Shell? previousActive = null;
        IShell? newShell = null;
        Exception? error = null;

        await slot!.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            previousActive = slot.Active;

            try
            {
                newShell = await CreateGenerationAsync(slot, blueprint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // FR-014: current active generation is unaffected; no partial entry retained.
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
    public async Task<IReadOnlyList<ReloadResult>> ReloadAllAsync(CancellationToken cancellationToken = default)
    {
        var names = GetBlueprintNames();
        if (names.Count == 0)
            return [];

        // Reload in parallel. Per-name failures are captured into ReloadResult.Error by ReloadAsync,
        // so Task.WhenAll never throws a per-name composition failure.
        var tasks = names.Select(async name =>
        {
            try
            {
                return await ReloadAsync(name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Covers the "no blueprint" / guard-clause throw paths. Composition errors are
                // already caught inside ReloadAsync.
                return new ReloadResult(name, NewShell: null, Drain: null, Error: ex);
            }
        }).ToList();

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shell);

        if (shell is not Shell typedShell)
            throw new ArgumentException(
                $"DrainAsync only accepts shells produced by this registry (CShells.Lifecycle.Shell); got {shell.GetType().FullName}.",
                nameof(shell));

        // Idempotent: first caller creates the operation; concurrent callers get the same instance.
        var op = _drainOps.GetOrAdd(shell, s => StartDrain((Shell)s));
        return Task.FromResult<IDrainOperation>(op);
    }

    private DrainOperation StartDrain(Shell shell)
    {
        var policy = ResolveDrainPolicy();
        var gracePeriod = ResolveGracePeriod();
        var op = new DrainOperation(shell, policy, gracePeriod, ResolveDrainLogger());

        // Transition to Draining (Active or Deactivating → Draining). Best-effort CAS.
        _ = shell.ForceAdvanceAsync(ShellLifecycleState.Draining);

        _ = op.RunAsync();
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

    /// <summary>
    /// Per-name state: the registered blueprint, a serialization semaphore for activate/reload,
    /// a generation counter, and the currently-active + historical shells.
    /// </summary>
    private sealed class NameSlot
    {
        // `volatile` not needed — all accesses to Blueprint go through Interlocked or under Semaphore.
        internal IShellBlueprint? Blueprint;

        internal readonly SemaphoreSlim Semaphore = new(1, 1);

        // Incremented under the Semaphore. `long` so the cast to int in ShellDescriptor is explicit.
        internal long NextGeneration;

        // Mutated under the Semaphore; read without locking by GetActive.
        internal volatile Shell? Active;

        // Immutable list; replaced under the Semaphore.
        internal ImmutableList<Shell> All = [];
    }
}
