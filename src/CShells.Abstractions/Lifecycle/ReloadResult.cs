namespace CShells.Lifecycle;

/// <summary>Per-name outcome of a reload operation.</summary>
/// <param name="Name">Shell name.</param>
/// <param name="NewShell">The newly-activated generation; null if composition/build/initialization failed.</param>
/// <param name="Drain">Drain operation on the previous generation; null if there was none or composition failed.</param>
/// <param name="Error">Non-null if blueprint composition, provider build, or any initializer threw.</param>
public sealed record ReloadResult(
    string Name,
    IShell? NewShell,
    IDrainOperation? Drain,
    Exception? Error);
