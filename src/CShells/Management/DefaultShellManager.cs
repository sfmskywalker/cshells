using CShells.Configuration;
using CShells.Features;
using CShells.Hosting;
using CShells.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Management;

/// <summary>
/// Default implementation of <see cref="IShellManager"/> that manages desired shell definitions
/// and reconciles them into committed applied runtimes.
/// </summary>
public class DefaultShellManager : IShellManager
{
    private readonly DefaultShellHost shellHost;
    private readonly IShellSettingsCache cache;
    private readonly IShellSettingsProvider provider;
    private readonly ShellRuntimeStateStore runtimeStateStore;
    private readonly RuntimeFeatureCatalog runtimeFeatureCatalog;
    private readonly IShellRuntimeStateAccessor runtimeStateAccessor;
    private readonly INotificationPublisher notificationPublisher;
    private readonly ILogger<DefaultShellManager> logger;
    private readonly SemaphoreSlim operationLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultShellManager"/> class.
    /// </summary>
    public DefaultShellManager(
        IShellHost shellHost,
        IShellHostInitializer shellHostInitializer,
        IShellSettingsCache cache,
        IShellSettingsProvider provider,
        INotificationPublisher notificationPublisher,
        ILogger<DefaultShellManager>? logger = null)
        : this(
            shellHost as DefaultShellHost ?? throw new ArgumentException("DefaultShellManager requires DefaultShellHost for reconciliation operations.", nameof(shellHost)),
            cache,
            provider,
            notificationPublisher,
            logger)
    {
        Guard.Against.Null(shellHostInitializer);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultShellManager"/> class.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public DefaultShellManager(
        DefaultShellHost shellHost,
        IShellSettingsCache cache,
        IShellSettingsProvider provider,
        INotificationPublisher notificationPublisher,
        ILogger<DefaultShellManager>? logger = null)
        : this(
            shellHost,
            cache,
            provider,
            shellHost.RuntimeStateStore,
            shellHost.RuntimeFeatureCatalog,
            new ShellRuntimeStateAccessor(shellHost.RuntimeStateStore),
            notificationPublisher,
            logger)
    {
    }

    internal DefaultShellManager(
        DefaultShellHost shellHost,
        IShellSettingsCache cache,
        IShellSettingsProvider provider,
        ShellRuntimeStateStore runtimeStateStore,
        RuntimeFeatureCatalog runtimeFeatureCatalog,
        IShellRuntimeStateAccessor runtimeStateAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<DefaultShellManager>? logger = null)
    {
        this.shellHost = Guard.Against.Null(shellHost);
        this.cache = Guard.Against.Null(cache);
        this.provider = Guard.Against.Null(provider);
        this.runtimeStateStore = Guard.Against.Null(runtimeStateStore);
        this.runtimeFeatureCatalog = Guard.Against.Null(runtimeFeatureCatalog);
        this.runtimeStateAccessor = Guard.Against.Null(runtimeStateAccessor);
        this.notificationPublisher = Guard.Against.Null(notificationPublisher);
        this.logger = logger ?? NullLogger<DefaultShellManager>.Instance;
    }

    internal async Task InitializeRuntimeAsync(CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            logger.LogInformation("Reconciling configured shells during application startup");

            var desiredShells = cache.GetAll().ToList();
            foreach (var settings in desiredShells)
            {
                runtimeStateStore.RecordDesired(settings);
            }

            var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            await ReconcileShellsAsync(desiredShells.Select(settings => settings.Id), snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddShellAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(settings);

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            logger.LogInformation("Adding desired shell '{ShellId}'", settings.Id);

            ReplaceShellInCache(settings);
            runtimeStateStore.RecordDesired(settings);

            await notificationPublisher.PublishAsync(new ShellAdded(settings), strategy: null, cancellationToken).ConfigureAwait(false);

            RuntimeFeatureCatalogSnapshot snapshot;
            try
            {
                snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                runtimeStateStore.MarkFailed(settings.Id, ex.Message);
                throw;
            }

            await ReconcileShellAsync(settings.Id, snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveShellAsync(ShellId shellId, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            logger.LogInformation("Removing desired shell '{ShellId}'", shellId);

            RemoveShellFromCache(shellId);
            await shellHost.RemoveAppliedRuntimeAsync(shellId, removeDesiredState: true, publishLifecycleNotifications: true, cancellationToken).ConfigureAwait(false);
            await notificationPublisher.PublishAsync(new ShellRemoved(shellId), strategy: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateShellAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(settings);

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            logger.LogInformation("Updating desired shell '{ShellId}'", settings.Id);

            ReplaceShellInCache(settings);
            runtimeStateStore.RecordDesired(settings);

            await notificationPublisher.PublishAsync(new ShellUpdated(settings), strategy: null, cancellationToken).ConfigureAwait(false);

            RuntimeFeatureCatalogSnapshot snapshot;
            try
            {
                snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                runtimeStateStore.MarkFailed(settings.Id, ex.Message);
                throw;
            }

            await ReconcileShellAsync(settings.Id, snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReloadShellAsync(ShellId shellId, CancellationToken cancellationToken = default)
    {
        await notificationPublisher.PublishAsync(new ShellReloading(shellId), strategy: null, cancellationToken).ConfigureAwait(false);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            logger.LogInformation("Reloading shell '{ShellId}' from provider", shellId);

            var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            var freshSettings = await provider.GetShellSettingsAsync(shellId, cancellationToken).ConfigureAwait(false);

            if (freshSettings is null)
            {
                logger.LogWarning("Provider does not define shell '{ShellId}'; reload aborted without state mutation", shellId);
                throw new InvalidOperationException($"Shell '{shellId}' is not defined by the provider. Reload aborted without modifying runtime state.");
            }

            ReplaceShellInCache(freshSettings);
            runtimeStateStore.RecordDesired(freshSettings);
            await ReconcileShellAsync(shellId, snapshot, cancellationToken).ConfigureAwait(false);

            await notificationPublisher.PublishAsync(
                new ShellReloaded(shellId, [shellId], runtimeStateAccessor.GetAllShells()),
                strategy: null,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReloadAllShellsAsync(CancellationToken cancellationToken = default)
    {
        await notificationPublisher.PublishAsync(new ShellReloading(null), strategy: null, cancellationToken).ConfigureAwait(false);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            logger.LogInformation("Reloading all shells from provider");

            var previousStatuses = runtimeStateAccessor.GetAllShells().ToDictionary(status => status.ShellId);
            var previousSettings = cache.GetAll().ToList();
            var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            var desiredShells = (await provider.GetShellSettingsAsync(cancellationToken).ConfigureAwait(false)).ToList();

            cache.Load(desiredShells);
            foreach (var settings in desiredShells)
            {
                runtimeStateStore.RecordDesired(settings);
            }

            var desiredIds = desiredShells.Select(settings => settings.Id).ToHashSet();
            var removedIds = previousSettings
                .Select(settings => settings.Id)
                .Where(shellId => !desiredIds.Contains(shellId))
                .Distinct()
                .ToList();

            foreach (var shellId in removedIds)
            {
                await shellHost.RemoveAppliedRuntimeAsync(shellId, removeDesiredState: true, publishLifecycleNotifications: true, cancellationToken).ConfigureAwait(false);
            }

            await ReconcileShellsAsync(desiredIds, snapshot, cancellationToken).ConfigureAwait(false);

            var statuses = runtimeStateAccessor.GetAllShells();
            var changedShells = DetermineChangedShells(previousStatuses, statuses, previousSettings, desiredShells, removedIds);

            foreach (var shellId in changedShells)
            {
                await notificationPublisher.PublishAsync(
                    new ShellReloaded(shellId, [shellId], statuses),
                    strategy: null,
                    cancellationToken).ConfigureAwait(false);
            }

            await notificationPublisher.PublishAsync(new ShellsReloaded(statuses), strategy: null, cancellationToken).ConfigureAwait(false);
            await notificationPublisher.PublishAsync(new ShellReloaded(null, changedShells, statuses), strategy: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task ReconcileShellsAsync(
        IEnumerable<ShellId> shellIds,
        RuntimeFeatureCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var shellId in shellIds.Distinct())
        {
            await ReconcileShellAsync(shellId, snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileShellAsync(
        ShellId shellId,
        RuntimeFeatureCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var record = runtimeStateStore.Get(shellId);
        if (record is null)
            return;

        var candidate = shellHost.BuildCandidate(record, snapshot);
        if (candidate.IsReadyToCommit)
        {
            await shellHost.CommitCandidateAsync(candidate, publishLifecycleNotifications: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (candidate.IsDeferred)
        {
            runtimeStateStore.MarkDeferred(shellId, candidate.MissingFeatures, candidate.FailureReason);
            logger.LogInformation(
                "Deferred shell '{ShellId}' at desired generation {DesiredGeneration}: {Reason}",
                shellId,
                record.DesiredGeneration,
                candidate.FailureReason);
            return;
        }

        runtimeStateStore.MarkFailed(shellId, candidate.FailureReason);
        logger.LogWarning(
            "Failed to reconcile shell '{ShellId}' at desired generation {DesiredGeneration}: {Reason}",
            shellId,
            record.DesiredGeneration,
            candidate.FailureReason);
    }

    private void ReplaceShellInCache(ShellSettings settings)
    {
        var existing = cache.GetAll().ToList();
        var index = existing.FindIndex(current => current.Id.Equals(settings.Id));

        if (index >= 0)
        {
            existing[index] = settings;
        }
        else
        {
            existing.Add(settings);
        }

        cache.Load(existing);
    }

    private void RemoveShellFromCache(ShellId shellId)
    {
        var remainingShells = cache.GetAll()
            .Where(settings => !settings.Id.Equals(shellId))
            .ToList();

        cache.Load(remainingShells);
    }

    private static IReadOnlyCollection<ShellId> DetermineChangedShells(
        IReadOnlyDictionary<ShellId, ShellRuntimeStatus> previousStatuses,
        IReadOnlyCollection<ShellRuntimeStatus> currentStatuses,
        IReadOnlyCollection<ShellSettings> previousSettings,
        IReadOnlyCollection<ShellSettings> currentSettings,
        IReadOnlyCollection<ShellId> removedIds)
    {
        var changedShells = new HashSet<ShellId>(removedIds);
        var previousSettingsById = previousSettings.ToDictionary(settings => settings.Id);
        var currentSettingsById = currentSettings.ToDictionary(settings => settings.Id);

        foreach (var shellId in previousSettingsById.Keys.Union(currentSettingsById.Keys))
        {
            var hadPreviousSettings = previousSettingsById.TryGetValue(shellId, out var previousSetting);
            var hasCurrentSettings = currentSettingsById.TryGetValue(shellId, out var currentSetting);

            if (!hadPreviousSettings || !hasCurrentSettings)
            {
                changedShells.Add(shellId);
                continue;
            }

            if (!ShellSettingsEqual(previousSetting!, currentSetting!))
            {
                changedShells.Add(shellId);
            }
        }

        foreach (var status in currentStatuses)
        {
            if (!previousStatuses.TryGetValue(status.ShellId, out var previousStatus) || previousStatus != status)
            {
                changedShells.Add(status.ShellId);
            }
        }

        return changedShells.ToList().AsReadOnly();
    }

    private static bool ShellSettingsEqual(ShellSettings left, ShellSettings right)
    {
        if (!left.Id.Equals(right.Id))
            return false;

        if (!left.EnabledFeatures.SequenceEqual(right.EnabledFeatures, StringComparer.OrdinalIgnoreCase))
            return false;

        if (left.ConfigurationData.Count != right.ConfigurationData.Count)
            return false;

        foreach (var pair in left.ConfigurationData)
        {
            if (!right.ConfigurationData.TryGetValue(pair.Key, out var otherValue) || !Equals(pair.Value, otherValue))
                return false;
        }

        return true;
    }
}
