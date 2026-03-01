using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Hosting;
using CShells.Workbench.Features.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CShells.Workbench.Features.Posts;

/// <summary>
/// Posts feature — adds blog post management.
/// Exposes GET /posts, GET /posts/{id}, and POST /posts.
/// </summary>
[ShellFeature("Posts", DependsOn = ["Core"], DisplayName = "Blog Posts")]
public class PostsFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Each shell gets its own isolated in-memory store
        services.AddSingleton<IPostRepository, InMemoryPostRepository>();

        // Seed a tenant-specific welcome post when the shell activates
        services.AddSingleton<IShellActivatedHandler, SeedPostsHandler>();
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet("/posts", (HttpContext ctx) =>
        {
            var repo   = ctx.RequestServices.GetRequiredService<IPostRepository>();
            var tenant = ctx.RequestServices.GetRequiredService<ITenantInfo>();
            return Results.Ok(new { tenant = tenant.TenantName, posts = repo.GetAll() });
        });
        endpoints.MapGet("/posts/{id:int}", (int id, HttpContext ctx) =>
        {
            var repo = ctx.RequestServices.GetRequiredService<IPostRepository>();
            var post = repo.GetById(id);
            return post is null ? Results.NotFound() : Results.Ok(post);
        });
        endpoints.MapPost("/posts", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CreatePostRequest>();
            if (req is null) return Results.BadRequest();
            var repo = ctx.RequestServices.GetRequiredService<IPostRepository>();
            var post = repo.Add(req.Title, req.Body, req.Author);
            return Results.Created($"/posts/{post.Id}", post);
        });
    }
}
public record CreatePostRequest(string Title, string Body, string Author);
