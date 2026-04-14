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
    private readonly IShellSettingsCache _cache;
    private readonly IShellSettingsProvider _provider;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<DefaultShellManager> _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultShellManager"/> class.
    /// </summary>
    public DefaultShellManager(
        IShellHost shellHost,
        IShellSettingsCache cache,
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

        // Capture the old shell context (if it was previously built) so we can
        // publish ShellDeactivating before disposing its service provider.
        ShellContext? oldContext = null;
        try
        {
            oldContext = _shellHost.GetShell(shellId);
        }
        catch (KeyNotFoundException)
        {
            // Shell was never built — no deactivation needed
        }

        // Deactivate the old shell before eviction (service provider is still alive)
        if (oldContext is not null)
        {
            _logger.LogDebug("Publishing ShellDeactivating for shell '{ShellId}' before reload eviction", shellId);
            await _notificationPublisher.PublishAsync(new ShellDeactivating(oldContext), strategy: null, cancellationToken);
        }

        lock (_lock)
        {
            // Replace the targeted shell in-place to preserve insertion order;
            // only append when the shell is genuinely new.
            var existing = _cache.GetAll().ToList();
            var index = existing.FindIndex(s => s.Id.Equals(shellId));

            if (index >= 0)
            {
                existing[index] = freshSettings;
            }
            else
            {
                existing.Add(freshSettings);
            }

            _cache.Load(existing);
        }

        // Evict the cached runtime context so next access rebuilds from fresh settings
        await _shellHost.EvictShellAsync(shellId);

        // Eagerly rebuild the shell and publish ShellActivated so lifecycle handlers run
        var newContext = _shellHost.GetShell(shellId);
        _logger.LogDebug("Publishing ShellActivated for shell '{ShellId}' after reload rebuild", shellId);
        await _notificationPublisher.PublishAsync(new ShellActivated(newContext), strategy: null, cancellationToken);

        _logger.LogInformation("Shell '{ShellId}' reloaded successfully", shellId);

        // Emit ShellReloaded on success (always last)
        await _notificationPublisher.PublishAsync(
            new ShellReloaded(shellId, [shellId]), strategy: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReloadAllShellsAsync(CancellationToken cancellationToken = default)
    {
        if (_shellHost is Hosting.DefaultShellHost defaultShellHost)
            await defaultShellHost.InitializeAsync(cancellationToken);

        _logger.LogInformation("Reloading all shells from provider");

        // Emit aggregate ShellReloading (null ShellId = full reload)
        await _notificationPublisher.PublishAsync(new ShellReloading(null), strategy: null, cancellationToken);

        // Load fresh shell settings from provider
        var settings = await _provider.GetShellSettingsAsync(cancellationToken);
        var settingsList = settings.ToList();

        // Capture current shells before updating cache for reconciliation
        IReadOnlyCollection<ShellSettings> previousShells;

        lock (_lock)
        {
            previousShells = _cache.GetAll();
        }

        // Determine changed shells (added, removed, or updated)
        var previousIds = previousShells.Select(s => s.Id).ToHashSet();
        var currentIds = settingsList.Select(s => s.Id).ToHashSet();

        var addedIds = currentIds.Except(previousIds);
        var removedIds = previousIds.Except(currentIds);
        var potentiallyUpdatedIds = currentIds.Intersect(previousIds);

        // Build lookup dictionaries using last-wins to handle duplicate IDs consistently
        // with ShellSettingsCache.Load() which also uses last-wins semantics.
        var previousByKey = new Dictionary<ShellId, ShellSettings>();
        foreach (var s in previousShells)
            previousByKey[s.Id] = s;

        var currentByKey = new Dictionary<ShellId, ShellSettings>();
        foreach (var s in settingsList)
            currentByKey[s.Id] = s;

        // For "updated", compare settings structurally to detect meaningful changes
        var updatedIds = potentiallyUpdatedIds.Where(id =>
            !ShellSettingsEqual(previousByKey[id], currentByKey[id]));

        var changedShells = addedIds.Concat(removedIds).Concat(updatedIds).ToList();

        // Emit per-shell ShellReloading for each changed shell (before mutation)
        foreach (var id in changedShells)
        {
            await _notificationPublisher.PublishAsync(new ShellReloading(id), strategy: null, cancellationToken);
        }

        // Capture all existing shell contexts before eviction so we can publish
        // ShellDeactivating while their service providers are still alive.
        // EvictAllShellsAsync disposes ALL contexts, not just changed ones.
        var existingContexts = new List<ShellContext>();
        foreach (var shell in previousShells)
        {
            try
            {
                existingContexts.Add(_shellHost.GetShell(shell.Id));
            }
            catch (KeyNotFoundException)
            {
                // Shell was never built — no deactivation needed
            }
        }

        // Deactivate all existing shells before eviction
        foreach (var context in existingContexts)
        {
            _logger.LogDebug("Publishing ShellDeactivating for shell '{ShellId}' before reload eviction", context.Id);
            await _notificationPublisher.PublishAsync(new ShellDeactivating(context), strategy: null, cancellationToken);
        }

        // Update cache - reconciles to provider state
        lock (_lock)
        {
            _cache.Clear();
            _cache.Load(settingsList);
        }

        // Evict all cached runtime contexts so next access rebuilds from fresh settings
        await _shellHost.EvictAllShellsAsync();

        // Eagerly rebuild all shells and publish ShellActivated for each
        var allShells = _shellHost.AllShells;
        foreach (var context in allShells)
        {
            _logger.LogDebug("Publishing ShellActivated for shell '{ShellId}' after reload rebuild", context.Id);
            await _notificationPublisher.PublishAsync(new ShellActivated(context), strategy: null, cancellationToken);
        }

        _logger.LogInformation("Reloaded {Count} shell(s)", settingsList.Count);

        // Emit per-shell ShellReloaded for each changed shell (after mutation)
        foreach (var id in changedShells)
        {
            await _notificationPublisher.PublishAsync(
                new ShellReloaded(id, [id]), strategy: null, cancellationToken);
        }

        // Publish ShellsReloaded (existing aggregate notification, preserved)
        await _notificationPublisher.PublishAsync(new ShellsReloaded(settingsList), strategy: null, cancellationToken);

        // Publish aggregate ShellReloaded last (null ShellId, with all changed shells)
        await _notificationPublisher.PublishAsync(
            new ShellReloaded(null, changedShells.AsReadOnly()), strategy: null, cancellationToken);
    }

    /// <summary>
    /// Compares two <see cref="ShellSettings"/> by value (Id, EnabledFeatures, ConfigurationData)
    /// to determine whether they represent the same logical configuration.
    /// </summary>
    private static bool ShellSettingsEqual(ShellSettings a, ShellSettings b)
    {
        if (!a.Id.Equals(b.Id))
            return false;

        if (!a.EnabledFeatures.SequenceEqual(b.EnabledFeatures, StringComparer.OrdinalIgnoreCase))
            return false;

        if (a.ConfigurationData.Count != b.ConfigurationData.Count)
            return false;

        foreach (var kvp in a.ConfigurationData)
        {
            if (!b.ConfigurationData.TryGetValue(kvp.Key, out var otherValue))
                return false;

            if (!Equals(kvp.Value, otherValue))
                return false;
        }

        return true;
    }
}
