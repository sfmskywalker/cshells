using CShells.Features;
using CShells.Hosting;
using CShells.Management;

namespace CShells.Management;

internal sealed record ShellRuntimeRecord(
    ShellId ShellId,
    long DesiredGeneration,
    ShellSettings DesiredSettings,
    long? AppliedGeneration,
    ShellSettings? AppliedSettings,
    RuntimeFeatureCatalogSnapshot? AppliedCatalog,
    ShellContext? AppliedContext,
    ShellReconciliationOutcome LatestDesiredOutcome,
    string? BlockingReason,
    IReadOnlyCollection<string> MissingFeatures)
{
    public bool HasAppliedRuntime => AppliedGeneration.HasValue && AppliedSettings is not null && AppliedCatalog is not null;

    public bool HasAppliedContext => AppliedContext is not null;
}

