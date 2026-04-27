using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CShells.Management.Api.Endpoints;

/// <summary>Handler for <c>POST /reload-all</c> — reload every active shell.</summary>
internal static class ReloadAllHandler
{
    public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
        group.MapPost("/reload-all", HandleAsync).WithName("ReloadAll");

    private static async Task<IResult> HandleAsync(
        int? maxDegreeOfParallelism,
        IShellRegistry registry,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            ReloadOptions options;
            if (maxDegreeOfParallelism is { } parallelism)
            {
                options = new(parallelism);
                options.EnsureValid();
            }
            else
            {
                options = new();
            }

            var results = await registry.ReloadActiveAsync(options, ct);
            return Results.Ok(results.Select(DtoMappers.MapReload).ToArray());
        }
        catch (Exception ex)
        {
            return ResultMapper.MapException(ex, ctx);
        }
    }
}
