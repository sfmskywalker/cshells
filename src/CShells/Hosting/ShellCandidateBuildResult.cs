using CShells.Features;

namespace CShells.Hosting;

internal sealed record ShellCandidateBuildResult(
    ShellId ShellId,
    long DesiredGeneration,
    ShellSettings DesiredSettings,
    RuntimeFeatureCatalogSnapshot CatalogSnapshot,
    ShellContext? CandidateContext,
    string? FailureReason,
    IReadOnlyCollection<string> MissingFeatures)
{
    public bool IsReadyToCommit => CandidateContext is not null;

    public bool IsDeferred => MissingFeatures.Count > 0;
}

