namespace CShells.Management;

/// <summary>
/// Describes the latest reconciliation outcome that determines whether a shell is currently serving,
/// deferred because required features are unavailable, or failed due to a non-missing-feature error.
/// </summary>
public enum ShellReconciliationOutcome
{
    Active,
    DeferredDueToMissingFeatures,
    Failed
}

