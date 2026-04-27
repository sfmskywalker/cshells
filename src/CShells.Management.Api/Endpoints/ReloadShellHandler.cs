using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CShells.Management.Api.Endpoints;

/// <summary>Handler for <c>POST /reload/{name}</c> — reload a single shell.</summary>
internal static class ReloadShellHandler
{
    public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
        group.MapPost("/reload/{name}", HandleAsync).WithName("ReloadShell");

    private static async Task<IResult> HandleAsync(
        string name,
        IShellRegistry registry,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var result = await registry.ReloadAsync(name, ct);
            return Results.Ok(DtoMappers.MapReload(result));
        }
        catch (Exception ex)
        {
            return ResultMapper.MapException(ex, ctx);
        }
    }
}
