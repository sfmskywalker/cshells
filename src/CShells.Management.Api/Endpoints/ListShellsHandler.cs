using CShells.Lifecycle;
using CShells.Management.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CShells.Management.Api.Endpoints;

/// <summary>Handler for <c>GET /</c> — paginated list of all shells.</summary>
internal static class ListShellsHandler
{
    public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
        group.MapGet("/", HandleAsync).WithName("ListShells");

    private static async Task<IResult> HandleAsync(
        string? cursor,
        int? pageSize,
        IShellRegistry registry,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var query = pageSize is { } size
                ? new ShellListQuery(Cursor: cursor, Limit: size)
                : new ShellListQuery(Cursor: cursor);
            query.EnsureValid();

            var page = await registry.ListAsync(query, ct);

            // Per-summary blueprint enrichment runs concurrently — for configuration-backed
            // providers each ComposeAsync re-reads IConfiguration, so a 100-row page would
            // otherwise serialize 100 lookups.
            var items = await Task.WhenAll(page.Items.Select(async summary =>
            {
                var blueprint = await DtoMappers.MapBlueprintAsync(
                    await registry.GetBlueprintAsync(summary.Name, ct), ct);
                var active = summary.ActiveGeneration is not null
                    ? registry.GetActive(summary.Name)
                    : null;
                return new ShellListItem(
                    Name: summary.Name,
                    Blueprint: blueprint,
                    Active: active is null ? null : DtoMappers.MapGeneration(active));
            }));

            return Results.Ok(new ShellPageResponse(items, page.NextCursor, query.Limit));
        }
        catch (Exception ex)
        {
            return ResultMapper.MapException(ex, ctx);
        }
    }
}
