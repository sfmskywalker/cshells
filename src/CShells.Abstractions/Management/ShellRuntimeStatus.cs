namespace CShells.Management;

/// <summary>
/// Represents the operator-visible status for one configured shell, including desired-versus-applied drift.
/// </summary>
/// <param name="ShellId">The shell identifier.</param>
/// <param name="DesiredGeneration">The latest configured desired generation.</param>
/// <param name="AppliedGeneration">The generation currently committed in the runtime, if any.</param>
/// <param name="Outcome">The current reconciliation outcome.</param>
/// <param name="IsInSync">Whether the applied generation matches the desired generation.</param>
/// <param name="IsRoutable">Whether the shell currently has a committed applied runtime.</param>
/// <param name="BlockingReason">The reason the latest desired generation is not currently applied.</param>
/// <param name="MissingFeatures">The required feature IDs still missing from the committed catalog, if any.</param>
public sealed record ShellRuntimeStatus(
    ShellId ShellId,
    long DesiredGeneration,
    long? AppliedGeneration,
    ShellReconciliationOutcome Outcome,
    bool IsInSync,
    bool IsRoutable,
    string? BlockingReason,
    IReadOnlyCollection<string> MissingFeatures);

