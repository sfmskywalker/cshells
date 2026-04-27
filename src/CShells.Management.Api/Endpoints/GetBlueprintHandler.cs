using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CShells.Management.Api.Endpoints;

/// <summary>Handler for <c>GET /{name}/blueprint</c> — registered blueprint without activating.</summary>
internal static class GetBlueprintHandler
{
    public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
        group.MapGet("/{name}/blueprint", HandleAsync).WithName("GetBlueprint");

    private static async Task<IResult> HandleAsync(
        string name,
        IShellRegistry registry,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var blueprint = await registry.GetBlueprintAsync(name, ct);
            if (blueprint is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"No blueprint registered for shell '{name}'.",
                    instance: ctx.Request.Path);
            }

            var dto = await DtoMappers.MapBlueprintAsync(blueprint, ct);
            return Results.Ok(dto);
        }
        catch (Exception ex)
        {
            return ResultMapper.MapException(ex, ctx);
        }
    }
}
