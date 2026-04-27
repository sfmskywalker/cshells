using System.Net;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using CShells.DependencyInjection;

namespace CShells.Tests.Integration.Management;

/// <summary>
/// Verifies that <c>MapShellManagementApi</c>'s <see cref="RouteGroupBuilder"/> return value
/// composes correctly with stock ASP.NET Core endpoint conventions (US5). The package
/// applies no auth of its own; hosts that chain <c>RequireAuthorization</c> /
/// <c>AddEndpointFilter</c> see them apply uniformly across all six routes.
/// </summary>
public class AuthorizationPassthroughTests
{
    [Fact(DisplayName = "Chained RequireAuthorization blocks unauthenticated access to all six routes")]
    public async Task RequireAuthorization_BlocksUnauthenticatedAccess_ToAllSixRoutes()
    {
        await using var fixture = new ManagementApiFixture(
            configureCShells: c => c.AddShell("acme", _ => { }),
            conventions: group =>
            {
                group.RequireAuthorization();
            });

        var endpoints = new[]
        {
            HttpMethod.Get,    // GET /
            HttpMethod.Get,    // GET /{name}
            HttpMethod.Get,    // GET /{name}/blueprint
            HttpMethod.Post,   // POST /reload/{name}
            HttpMethod.Post,   // POST /reload-all
            HttpMethod.Post,   // POST /{name}/force-drain
        };
        var paths = new[]
        {
            "/admin/",
            "/admin/acme",
            "/admin/acme/blueprint",
            "/admin/reload/acme",
            "/admin/reload-all",
            "/admin/acme/force-drain",
        };

        for (var i = 0; i < endpoints.Length; i++)
        {
            using var req = new HttpRequestMessage(endpoints[i], paths[i]);
            var response = await fixture.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact(DisplayName = "AddEndpointFilter runs before the endpoint handler")]
    public async Task AddEndpointFilter_RunsBeforeHandler()
    {
        var counter = new CallCounterFilter();

        await using var fixture = new ManagementApiFixture(
            configureCShells: c => c.AddShell("acme", _ => { }),
            conventions: group =>
            {
                group.AddEndpointFilter(counter);
            });

        var response = await fixture.GetAsync("/admin/");
        response.EnsureSuccessStatusCode();

        Assert.Equal(1, counter.Calls);
    }

    private sealed class CallCounterFilter : IEndpointFilter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
        {
            Interlocked.Increment(ref _calls);
            return next(ctx);
        }
    }
}
