using System.Collections.Concurrent;
namespace CShells.Workbench.Features.Posts;
/// <summary>
/// Simple, thread-safe in-memory post store.
/// Each shell gets its own isolated instance.
/// Data is seeded via <see cref="SeedPostsHandler"/> on shell activation.
/// </summary>
public class InMemoryPostRepository : IPostRepository
{
    private readonly ConcurrentDictionary<int, Post> _posts = new();
    private int _nextId = 1;

    public IReadOnlyList<Post> GetAll() => [.. _posts.Values.OrderBy(p => p.Id)];
    public Post? GetById(int id) => _posts.GetValueOrDefault(id);
    public Post Add(string title, string body, string author)
    {
        var id = Interlocked.Increment(ref _nextId) - 1;
        var post = new Post(id, title, body, author, DateTimeOffset.UtcNow);
        _posts[id] = post;
        return post;
    }
}
