namespace CShells.Management;

internal sealed class ShellRuntimeStateAccessor(ShellRuntimeStateStore stateStore) : IShellRuntimeStateAccessor
{
    private readonly ShellRuntimeStateStore stateStore = Guard.Against.Null(stateStore);

    public IReadOnlyCollection<ShellRuntimeStatus> GetAllShells() =>
        stateStore
            .GetAll()
            .Select(Project)
            .OrderBy(status => status.ShellId.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

    public ShellRuntimeStatus? GetShell(ShellId shellId)
    {
        var record = stateStore.Get(shellId);
        return record is null ? null : Project(record);
    }

    private static ShellRuntimeStatus Project(ShellRuntimeRecord record)
    {
        var isRoutable = record.HasAppliedRuntime;
        var isInSync = record.AppliedGeneration.HasValue && record.AppliedGeneration.Value == record.DesiredGeneration;
        var blockingReason = isInSync ? null : record.BlockingReason;
        var outcome = isRoutable ? ShellReconciliationOutcome.Active : record.LatestDesiredOutcome;

        return new ShellRuntimeStatus(
            record.ShellId,
            record.DesiredGeneration,
            record.AppliedGeneration,
            outcome,
            isInSync,
            isRoutable,
            blockingReason,
            [.. record.MissingFeatures]);
    }
}

