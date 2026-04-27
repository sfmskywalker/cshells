namespace CShells.Management.Api.Models;

/// <summary>
/// Per-shell outcome of a reload operation, returned both as the body of
/// <c>POST /reload/{name}</c> and as one entry in the array body of <c>POST /reload-all</c>.
/// </summary>
internal sealed record ReloadResultResponse(
    string Name,
    bool Success,
    ShellGenerationResponse? NewShell,
    DrainSnapshot? Drain,
    ErrorDescription? Error);

/// <summary>
/// Summary of an exception captured during reload. Carries the exception type's simple name
/// and message; stack traces are not exposed.
/// </summary>
internal sealed record ErrorDescription(string Type, string Message);
