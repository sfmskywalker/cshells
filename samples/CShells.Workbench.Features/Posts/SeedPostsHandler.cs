using CShells.Lifecycle;
using CShells.Workbench.Features.Core;

namespace CShells.Workbench.Features.Posts;

/// <summary>
/// Seeds each tenant with a personalised welcome post when the shell activates.
/// Demonstrates <see cref="IShellInitializer"/> for per-shell startup work.
/// </summary>
public class SeedPostsHandler(IPostRepository repo, ITenantInfo tenant) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        repo.Add(
            $"Welcome to {tenant.TenantName}",
            $"This is the {tenant.Plan} plan. Enjoy your blog!",
            "System");

        return Task.CompletedTask;
    }
}
