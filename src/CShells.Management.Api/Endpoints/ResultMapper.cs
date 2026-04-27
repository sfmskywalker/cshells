using CShells.Lifecycle;
using Microsoft.AspNetCore.Http;

namespace CShells.Management.Api.Endpoints;

/// <summary>
/// Translates registry exceptions to RFC 7807 problem-details responses per FR-013. The
/// catch-all default returns a 500 — the registry's documented exceptions are the only paths
/// the management API expects to handle.
/// </summary>
internal static class ResultMapper
{
    public static IResult MapException(Exception ex, HttpContext context) => ex switch
    {
        ShellBlueprintNotFoundException notFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: notFound.Message,
            instance: context.Request.Path),

        ShellBlueprintUnavailableException unavailable => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Service Unavailable",
            detail: unavailable.Message,
            instance: context.Request.Path),

        OperationCanceledException => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Service Unavailable",
            detail: "Host is shutting down.",
            instance: context.Request.Path),

        ArgumentOutOfRangeException oor => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: oor.Message,
            instance: context.Request.Path),

        _ => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            detail: ex.Message,
            instance: context.Request.Path),
    };
}
