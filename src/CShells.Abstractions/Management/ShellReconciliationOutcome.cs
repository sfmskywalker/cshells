namespace CShells.Management;

/// <summary>
/// Describes the latest reconciliation outcome for a shell.
/// </summary>
public enum ShellReconciliationOutcome
{
    /// <summary>
    /// The shell activated successfully with all configured features present.
    /// </summary>
    Active,

    /// <summary>
    /// The shell activated successfully with available features, but one or more configured
    /// features were not present in the catalog. The shell is fully operational for its
    /// loaded features. Missing features are recorded on the shell's status and context.
    /// </summary>
    ActiveWithMissingFeatures,

    /// <summary>
    /// The shell could not be built due to an error unrelated to missing features
    /// (e.g. DI wiring error, circular dependency).
    /// </summary>
    Failed
}
