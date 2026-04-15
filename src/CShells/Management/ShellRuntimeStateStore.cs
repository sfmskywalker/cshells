using CShells.Features;
using CShells.Hosting;
using CShells.Management;

namespace CShells.Management;

internal sealed class ShellRuntimeStateStore
{
    private readonly Dictionary<ShellId, ShellRuntimeRecord> records = [];
    private readonly object syncRoot = new();

    public IReadOnlyCollection<ShellRuntimeRecord> GetAll()
    {
        lock (syncRoot)
        {
            return [.. records.Values];
        }
    }

    public ShellRuntimeRecord? Get(ShellId shellId)
    {
        lock (syncRoot)
        {
            return records.GetValueOrDefault(shellId);
        }
    }

    public bool HasDesiredShell(ShellId shellId)
    {
        lock (syncRoot)
        {
            return records.ContainsKey(shellId);
        }
    }

    public ShellRuntimeRecord RecordDesired(ShellSettings settings)
    {
        Guard.Against.Null(settings);

        lock (syncRoot)
        {
            var clonedSettings = CloneShellSettings(settings);
            var existing = records.GetValueOrDefault(settings.Id);
            var desiredGeneration = existing is null
                ? 1
                : ShellSettingsEqual(existing.DesiredSettings, clonedSettings)
                    ? existing.DesiredGeneration
                    : existing.DesiredGeneration + 1;

            var isGenerationAdvanced = existing is not null && desiredGeneration > existing.DesiredGeneration;
            var record = existing is null
                ? new ShellRuntimeRecord(
                    clonedSettings.Id,
                    desiredGeneration,
                    clonedSettings,
                    null,
                    null,
                    null,
                    null,
                    ShellReconciliationOutcome.Failed,
                    "Shell has not been reconciled yet.",
                    [])
                : existing with
                {
                    DesiredGeneration = desiredGeneration,
                    DesiredSettings = clonedSettings,
                    BlockingReason = isGenerationAdvanced ? "Awaiting reconciliation." : existing.BlockingReason,
                    MissingFeatures = isGenerationAdvanced ? [] : existing.MissingFeatures
                };

            records[settings.Id] = record;
            return record;
        }
    }

    public ShellRuntimeRecord MarkDeferred(ShellId shellId, IReadOnlyCollection<string> missingFeatures, string? blockingReason)
    {
        lock (syncRoot)
        {
            var existing = GetRequiredRecord(shellId);
            var normalizedMissingFeatures = missingFeatures
                .Where(feature => !string.IsNullOrWhiteSpace(feature))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var record = existing with
            {
                LatestDesiredOutcome = ShellReconciliationOutcome.DeferredDueToMissingFeatures,
                BlockingReason = blockingReason,
                MissingFeatures = normalizedMissingFeatures
            };

            records[shellId] = record;
            return record;
        }
    }

    public ShellRuntimeRecord MarkFailed(ShellId shellId, string? blockingReason)
    {
        lock (syncRoot)
        {
            var existing = GetRequiredRecord(shellId);
            var record = existing with
            {
                LatestDesiredOutcome = ShellReconciliationOutcome.Failed,
                BlockingReason = blockingReason,
                MissingFeatures = []
            };

            records[shellId] = record;
            return record;
        }
    }

    public ShellCommitResult CommitAppliedRuntime(
        ShellId shellId,
        ShellSettings appliedSettings,
        RuntimeFeatureCatalogSnapshot appliedCatalog,
        ShellContext appliedContext)
    {
        Guard.Against.Null(appliedSettings);
        Guard.Against.Null(appliedCatalog);
        Guard.Against.Null(appliedContext);

        lock (syncRoot)
        {
            var existing = GetRequiredRecord(shellId);
            var previousContext = existing.AppliedContext;
            var record = existing with
            {
                AppliedGeneration = existing.DesiredGeneration,
                AppliedSettings = CloneShellSettings(appliedSettings),
                AppliedCatalog = appliedCatalog,
                AppliedContext = appliedContext,
                LatestDesiredOutcome = ShellReconciliationOutcome.Active,
                BlockingReason = null,
                MissingFeatures = []
            };

            records[shellId] = record;
            return new(record, previousContext);
        }
    }

    public ShellContext? EvictAppliedContext(ShellId shellId)
    {
        lock (syncRoot)
        {
            if (!records.TryGetValue(shellId, out var record) || record.AppliedContext is null)
                return null;

            var previousContext = record.AppliedContext;
            records[shellId] = record with { AppliedContext = null };
            return previousContext;
        }
    }

    public IReadOnlyCollection<ShellContext> EvictAllAppliedContexts()
    {
        lock (syncRoot)
        {
            var contexts = records.Values
                .Where(record => record.AppliedContext is not null)
                .Select(record => record.AppliedContext!)
                .ToList();

            foreach (var record in records.Values.ToList())
            {
                if (record.AppliedContext is null)
                    continue;

                records[record.ShellId] = record with { AppliedContext = null };
            }

            return contexts;
        }
    }

    public ShellRuntimeRecord SetAppliedContext(ShellId shellId, ShellContext context)
    {
        Guard.Against.Null(context);

        lock (syncRoot)
        {
            var existing = GetRequiredRecord(shellId);
            var record = existing with { AppliedContext = context };
            records[shellId] = record;
            return record;
        }
    }

    public ShellRemovalResult RemoveShell(ShellId shellId)
    {
        lock (syncRoot)
        {
            if (!records.Remove(shellId, out var record))
                return new(null, null);

            return new(record, record.AppliedContext);
        }
    }

    public ShellRemovalResult ClearAppliedRuntime(ShellId shellId)
    {
        lock (syncRoot)
        {
            if (!records.TryGetValue(shellId, out var existing))
                return new(null, null);

            var record = existing with
            {
                AppliedGeneration = null,
                AppliedSettings = null,
                AppliedCatalog = null,
                AppliedContext = null
            };

            records[shellId] = record;
            return new(record, existing.AppliedContext);
        }
    }

    private ShellRuntimeRecord GetRequiredRecord(ShellId shellId) =>
        records.GetValueOrDefault(shellId)
        ?? throw new KeyNotFoundException($"Shell '{shellId}' is not present in the runtime state store.");

    private static ShellSettings CloneShellSettings(ShellSettings settings)
    {
        var clone = new ShellSettings
        {
            Id = settings.Id,
            EnabledFeatures = [.. settings.EnabledFeatures],
            ConfigurationData = settings.ConfigurationData.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var configurator in settings.FeatureConfigurators)
        {
            clone.FeatureConfigurators[configurator.Key] = configurator.Value;
        }

        return clone;
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

internal sealed record ShellCommitResult(ShellRuntimeRecord Record, ShellContext? PreviousContext);

internal sealed record ShellRemovalResult(ShellRuntimeRecord? Record, ShellContext? PreviousContext);

