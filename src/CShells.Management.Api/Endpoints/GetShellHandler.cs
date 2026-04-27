using CShells.Lifecycle;
using CShells.Management.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CShells.Management.Api.Endpoints;

/// <summary>Handler for <c>GET /{name}</c> — focused view of one shell.</summary>
internal static class GetShellHandler
{
    public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
        group.MapGet("/{name}", HandleAsync).WithName("GetShell");

    private static async Task<IResult> HandleAsync(
        string name,
        IShellRegistry registry,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var blueprint = await registry.GetBlueprintAsync(name, ct);

            // Per FR-011, surface every generation NOT yet disposed. The registry retains
            // disposed shells in its in-memory slot, but they are no longer "live" from the
            // operator's perspective.
            var liveGenerations = registry.GetAll(name)
                .Where(s => s.State != ShellLifecycleState.Disposed)
                .ToArray();

            if (blueprint is null && liveGenerations.Length == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"Shell '{name}' has no blueprint and no live generations.",
                    instance: ctx.Request.Path);
            }

            var blueprintDto = await DtoMappers.MapBlueprintAsync(blueprint, ct);
            var generationsDto = liveGenerations.Select(DtoMappers.MapGeneration).ToArray();

            return Results.Ok(new ShellDetailResponse(name, blueprintDto, generationsDto));
        }
        catch (Exception ex)
        {
            return ResultMapper.MapException(ex, ctx);
        }
    }
}
