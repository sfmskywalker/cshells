using CShells.Configuration;
using CShells.Hosting;
using CShells.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Management;

/// <summary>
/// Default implementation of <see cref="IShellManager"/> that manages shell lifecycle
/// and publishes notifications for shell state changes.
/// </summary>
public class DefaultShellManager : IShellManager
{
    private readonly IShellHost _shellHost;
    private readonly ShellSettingsCache _cache;
    private readonly IShellSettingsProvider _provider;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<DefaultShellManager> _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultShellManager"/> class.
    /// </summary>
    public DefaultShellManager(
        IShellHost shellHost,
        ShellSettingsCache cache,
        IShellSettingsProvider provider,
        INotificationPublisher notificationPublisher,
        ILogger<DefaultShellManager>? logger = null)
    {
        _shellHost = shellHost;
        _cache = cache;
        _provider = provider;
        _notificationPublisher = notificationPublisher;
        _logger = logger ?? NullLogger<DefaultShellManager>.Instance;
    }

    /// <inheritdoc />
    public async Task AddShellAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(settings);

        ShellContext shellContext;

        lock (_lock)
        {
            _logger.LogInformation("Adding shell '{ShellId}'", settings.Id);

            // Add to cache
            _cache.Load(_cache.GetAll().Append(settings));

            // Build shell context (this triggers feature service registration)
            shellContext = _shellHost.GetShell(settings.Id);

            _logger.LogInformation("Shell '{ShellId}' added successfully", settings.Id);
        }

        // Publish notifications (outside lock to avoid deadlocks)
        // Activate shell first, then notify that it was added
        await _notificationPublisher.PublishAsync(new ShellActivated(shellContext), strategy: null, cancellationToken);
        await _notificationPublisher.PublishAsync(new ShellAdded(settings), strategy: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveShellAsync(ShellId shellId, CancellationToken cancellationToken = default)
    {
        ShellContext? shellContext = null;

        // Get shell context BEFORE removing from cache so handlers can access it during deactivation
        try
        {
            shellContext = _shellHost.GetShell(shellId);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Shell '{ShellId}' not found, skipping deactivation", shellId);
        }

        // Publish deactivation notification BEFORE removal (outside lock to avoid deadlocks)
        if (shellContext != null)
        {
            await _notificationPublisher.PublishAsync(new ShellDeactivating(shellContext), strategy: null, cancellationToken);
        }

        lock (_lock)
        {
            _logger.LogInformation("Removing shell '{ShellId}'", shellId);

            // Remove from cache
            var remainingShells = _cache.GetAll().Where(s => !s.Id.Equals(shellId));
            _cache.Clear();
            _cache.Load(remainingShells);

            _logger.LogInformation("Shell '{ShellId}' removed successfully", shellId);
        }

        // Publish removal notification AFTER removal (outside lock to avoid deadlocks)
        await _notificationPublisher.PublishAsync(new ShellRemoved(shellId), strategy: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateShellAsync(ShellSettings settings, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(settings);

        _logger.LogInformation("Updating shell '{ShellId}'", settings.Id);

        // Remove existing shell
        await RemoveShellAsync(settings.Id, cancellationToken);

        // Add updated shell
        await AddShellAsync(settings, cancellationToken);

        _logger.LogInformation("Shell '{ShellId}' updated successfully", settings.Id);
    }

    /// <inheritdoc />
    public async Task ReloadShellAsync(ShellId shellId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading shell '{ShellId}' from provider", shellId);

        // Emit ShellReloading before any state mutation
        await _notificationPublisher.PublishAsync(new ShellReloading(shellId), strategy: null, cancellationToken);

        // Query the provider for the targeted shell
        var freshSettings = await _provider.GetShellSettingsAsync(shellId, cancellationToken);

        if (freshSettings is null)
        {
            _logger.LogWarning("Provider does not define shell '{ShellId}'; reload aborted without state mutation", shellId);
            throw new InvalidOperationException($"Shell '{shellId}' is not defined by the provider. Reload aborted without modifying runtime state.");
        }

        lock (_lock)
        {
            // Update cache: replace the targeted shell, preserve all others
            _cache.Load(_cache.GetAll().Where(s => !s.Id.Equals(shellId)).Append(freshSettings));
        }

        // Invalidate the cached runtime context so next access rebuilds
        if (_shellHost is DefaultShellHost defaultHost)
        {
            await defaultHost.InvalidateShellContextAsync(shellId);
        }

        _logger.LogInformation("Shell '{ShellId}' reloaded successfully", shellId);

        // Emit ShellReloaded on success (always last)
        await _notificationPublisher.PublishAsync(
            new ShellReloaded(shellId, [shellId]), strategy: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReloadAllShellsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading all shells from provider");

        // Emit aggregate ShellReloading (null ShellId = full reload)
        await _notificationPublisher.PublishAsync(new ShellReloading(null), strategy: null, cancellationToken);

        // Load fresh shell settings from provider
        var settings = await _provider.GetShellSettingsAsync(cancellationToken);
        var settingsList = settings.ToList();

        // Capture current shell IDs before updating cache for reconciliation
        IReadOnlyCollection<ShellSettings> previousShells;

        lock (_lock)
        {
            previousShells = _cache.GetAll();

            // Update cache - reconciles to provider state
            _cache.Clear();
            _cache.Load(settingsList);
        }

        // Determine changed shells (added, removed, or updated)
        var previousIds = previousShells.Select(s => s.Id).ToHashSet();
        var currentIds = settingsList.Select(s => s.Id).ToHashSet();

        var addedIds = currentIds.Except(previousIds);
        var removedIds = previousIds.Except(currentIds);
        var potentiallyUpdatedIds = currentIds.Intersect(previousIds);

        // For "updated", compare settings objects by reference to detect changes
        var previousByKey = previousShells.ToDictionary(s => s.Id);
        var currentByKey = settingsList.ToDictionary(s => s.Id);
        var updatedIds = potentiallyUpdatedIds.Where(id =>
            !ReferenceEquals(previousByKey[id], currentByKey[id]));

        var changedShells = addedIds.Concat(removedIds).Concat(updatedIds).ToList();

        // Invalidate all cached runtime contexts so next access rebuilds from fresh settings
        if (_shellHost is DefaultShellHost defaultHost)
        {
            await defaultHost.InvalidateAllShellContextsAsync();
        }

        _logger.LogInformation("Reloaded {Count} shell(s)", settingsList.Count);

        // Publish ShellsReloaded (existing aggregate notification, preserved)
        await _notificationPublisher.PublishAsync(new ShellsReloaded(settingsList), strategy: null, cancellationToken);

        // Publish aggregate ShellReloaded last (null ShellId, with all changed shells)
        await _notificationPublisher.PublishAsync(
            new ShellReloaded(null, changedShells.AsReadOnly()), strategy: null, cancellationToken);
    }
}
