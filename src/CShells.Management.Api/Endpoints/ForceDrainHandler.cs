using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CShells.Management.Api.Endpoints;

/// <summary>
/// Handler for <c>POST /{name}/force-drain</c> — forces every in-flight drain on the named
/// shell to terminate immediately. Returns one entry per forced generation.
/// </summary>
internal static class ForceDrainHandler
{
    public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
        group.MapPost("/{name}/force-drain", HandleAsync).WithName("ForceDrain");

    private static async Task<IResult> HandleAsync(
        string name,
        IShellRegistry registry,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var allGenerations = registry.GetAll(name);

            // Only consult the blueprint provider when no generations exist — that's the
            // only path that needs the blueprint to distinguish "registered but inactive"
            // from "completely unknown". When generations exist, the shell's identity is
            // already established by the in-memory registry, and force-drain (an emergency
            // operation) MUST NOT be blocked by a transiently unavailable blueprint store.
            if (allGenerations.Count == 0)
            {
                var blueprint = await registry.GetBlueprintAsync(name, ct);
                if (blueprint is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Not Found",
                        detail: $"Shell '{name}' is not known to the registry.",
                        instance: ctx.Request.Path);
                }
            }

            // Filter to in-flight drains (Deactivating/Draining). Drained generations are
            // already terminal and Disposed generations have null Drain anyway.
            var inFlight = allGenerations
                .Where(s => s.State is ShellLifecycleState.Deactivating or ShellLifecycleState.Draining)
                .ToArray();

            if (inFlight.Length == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"No in-flight drain to force for shell '{name}'.",
                    instance: ctx.Request.Path);
            }

            // Force every in-flight drain in parallel; await each to a terminal state. The
            // Drain reference can race with natural completion or with the registry's
            // Disposed-transition clear-then-advance, so a generation snapshotted as
            // Deactivating/Draining may have a null Drain by the time we read it. Skip those
            // entries — the drain effectively completed itself before we got there.
            var results = await Task.WhenAll(inFlight.Select(async shell =>
            {
                var op = shell.Drain;
                if (op is null)
                    return null;
                await op.ForceAsync(ct);
                return (DrainResult?)await op.WaitAsync(ct);
            }));

            return Results.Ok(results
                .OfType<DrainResult>()
                .Select(DtoMappers.MapDrainResult)
                .ToArray());
        }
        catch (Exception ex)
        {
            return ResultMapper.MapException(ex, ctx);
        }
    }
}
