using CShells.AspNetCore.Routing;
using CShells.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace CShells.Tests.Unit.AspNetCore.Routing;

/// <summary>
/// Unit tests for <see cref="DefaultShellRouteIndex"/>. Covers the path-by-name fast path,
/// lazy snapshot population for non-name modes, lifecycle invalidation, root-path ambiguity,
/// duplicate-path handling, and initial-population failure → unavailable exception.
/// </summary>
public class DefaultShellRouteIndexTests
{
    [Fact(DisplayName = "Path-by-name lookup hits provider once, never enumerates the catalogue")]
    public async Task PathByName_UsesGetAsync_NotListAsync()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var index = new DefaultShellRouteIndex(provider);

        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: "acme", IsRootPath: false, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.NotNull(match);
        Assert.Equal("acme", match!.ShellId.Name);
        Assert.Equal(ShellRoutingMode.Path, match.MatchedMode);
        Assert.Equal(1, provider.LookupCount);
        Assert.Equal(0, provider.ListCount);
    }

    [Fact(DisplayName = "Path-by-name returns null when blueprint exists but WebRouting:Path differs from segment")]
    public async Task PathByName_RejectsBlueprintWithDifferentPath()
    {
        // Convention: path-by-name match requires WebRouting:Path == segment.
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "elsewhere"));

        var index = new DefaultShellRouteIndex(provider);

        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: "acme", IsRootPath: false, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.Null(match);
    }

    [Fact(DisplayName = "Path-by-name returns null when blueprint exists but declares no WebRouting:Path")]
    public async Task PathByName_RejectsBlueprintWithoutPath()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme"); // no WebRouting:Path configured

        var index = new DefaultShellRouteIndex(provider);

        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: "acme", IsRootPath: false, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.Null(match);
    }

    [Fact(DisplayName = "Root-path lookup returns single root-eligible blueprint")]
    public async Task RootPath_SingleClaimant_Resolves()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""))
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var index = new DefaultShellRouteIndex(provider);

        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: true, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.NotNull(match);
        Assert.Equal("Default", match!.ShellId.Name);
        Assert.Equal(ShellRoutingMode.RootPath, match.MatchedMode);
    }

    [Fact(DisplayName = "Root-path lookup returns null when multiple blueprints opt in (ambiguous)")]
    public async Task RootPath_AmbiguousClaimants_ReturnsNull()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("First", b => b.WithConfiguration("WebRouting:Path", ""))
            .Add("Second", b => b.WithConfiguration("WebRouting:Path", ""));

        var index = new DefaultShellRouteIndex(provider);

        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: true, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.Null(match);
    }

    [Fact(DisplayName = "Host-mode lookup returns matching blueprint")]
    public async Task Host_ResolvesByConfigValue()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Host", "acme.example.com"));

        var index = new DefaultShellRouteIndex(provider);

        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: false, Host: "acme.example.com",
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.NotNull(match);
        Assert.Equal("acme", match!.ShellId.Name);
        Assert.Equal(ShellRoutingMode.Host, match.MatchedMode);
    }

    [Fact(DisplayName = "Header-mode lookup matches header value to shell name")]
    public async Task Header_MatchesByShellName()
    {
        // Header-mode convention: header VALUE equals shell name; blueprint declares WebRouting:HeaderName.
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:HeaderName", "X-Tenant"));

        var index = new DefaultShellRouteIndex(provider);

        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: false, Host: null,
            HeaderName: "X-Tenant", HeaderValue: "acme", ClaimKey: null, ClaimValue: null));

        Assert.NotNull(match);
        Assert.Equal("acme", match!.ShellId.Name);
        Assert.Equal(ShellRoutingMode.Header, match.MatchedMode);
    }

    [Fact(DisplayName = "Initial population failure surfaces ShellRouteIndexUnavailableException for non-name modes")]
    public async Task InitialPopulationFailure_ThrowsUnavailable()
    {
        var provider = new StubShellBlueprintProvider();
        provider.ThrowOnList = new InvalidOperationException("DB unreachable");

        var index = new DefaultShellRouteIndex(provider);

        var ex = await Assert.ThrowsAsync<ShellRouteIndexUnavailableException>(async () =>
            await index.TryMatchAsync(new ShellRouteCriteria(
                PathFirstSegment: null, IsRootPath: true, Host: null,
                HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null)));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact(DisplayName = "Rebuild failure after Invalidate falls back to previous snapshot (last-good)")]
    public async Task RebuildFailure_AfterInvalidate_ServesLastGood()
    {
        // Provider succeeds on the first build, then starts failing. After Invalidate the
        // index must NOT surface ShellRouteIndexUnavailableException — it must transparently
        // serve the previously-built snapshot. This is the reliability guarantee that
        // motivated dropping the eager "clear _snapshot on Invalidate" pattern.
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""));

        var index = new DefaultShellRouteIndex(provider);

        var rootCriteria = new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: true, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null);

        // Initial build succeeds.
        var first = await index.TryMatchAsync(rootCriteria);
        Assert.NotNull(first);
        Assert.Equal("Default", first!.ShellId.Name);

        // Provider goes down; lifecycle invalidates the index.
        provider.ThrowOnList = new InvalidOperationException("DB unreachable");
        index.Invalidate();

        // Routing keeps working against the previously-good snapshot.
        var second = await index.TryMatchAsync(rootCriteria);
        Assert.NotNull(second);
        Assert.Equal("Default", second!.ShellId.Name);
    }

    [Fact(DisplayName = "Initial population failure does NOT degrade path-by-name lookups")]
    public async Task InitialPopulationFailure_PathByNameStillWorks()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));
        provider.ThrowOnList = new InvalidOperationException("DB unreachable");

        var index = new DefaultShellRouteIndex(provider);

        // Path-by-name doesn't need the snapshot — it goes straight through GetAsync.
        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: "acme", IsRootPath: false, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.NotNull(match);
        Assert.Equal("acme", match!.ShellId.Name);
    }

    [Fact(DisplayName = "Snapshot is reused across calls — provider listed only once")]
    public async Task Snapshot_PopulatedOnce_ThenCached()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""));

        var index = new DefaultShellRouteIndex(provider);

        var rootCriteria = new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: true, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null);

        await index.TryMatchAsync(rootCriteria);
        await index.TryMatchAsync(rootCriteria);
        await index.TryMatchAsync(rootCriteria);

        Assert.Equal(1, provider.ListCount);
    }

    [Fact(DisplayName = "Invalidate forces snapshot rebuild on next non-name-mode call")]
    public async Task Invalidate_TriggersRebuild()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""));

        var index = new DefaultShellRouteIndex(provider);

        var rootCriteria = new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: true, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null);

        await index.TryMatchAsync(rootCriteria);
        Assert.Equal(1, provider.ListCount);

        // Add a new blueprint and invalidate; next call should rebuild.
        provider.Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));
        index.Invalidate();

        await index.TryMatchAsync(rootCriteria);
        Assert.Equal(2, provider.ListCount);
    }

    [Fact(DisplayName = "GetCandidateSnapshot returns up to maxEntries; over-cap requests don't trigger population")]
    public void GetCandidateSnapshot_DoesNotPopulate()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var index = new DefaultShellRouteIndex(provider);

        var snapshot = index.GetCandidateSnapshot(10);

        Assert.Empty(snapshot.Entries);
        Assert.Equal(0, snapshot.Total);
        Assert.Equal(0, provider.ListCount);
    }

    [Fact(DisplayName = "Path != ShellName warns at population time and is silently inert for path mode")]
    public async Task PathDifferentFromName_WarnsAndDoesNotMatch()
    {
        // Convention enforcement: TryMatchByPathSegmentAsync looks up the blueprint by name
        // equal to the request's first path segment, so a blueprint declaring
        // WebRouting:Path != Name is unreachable via path mode. The builder must surface
        // this misconfiguration at startup rather than letting the request silently 404.
        var provider = new StubShellBlueprintProvider()
            .Add("acme-corp", b => b.WithConfiguration("WebRouting:Path", "acme"));

        var captured = new List<(LogLevel Level, string Message)>();
        var logger = new CapturingLogger<DefaultShellRouteIndex>(captured);

        var index = new DefaultShellRouteIndex(provider, logger);

        // Path-by-name lookup for "acme" — the provider has no blueprint named "acme",
        // only "acme-corp" with Path="acme". Returns null per the convention.
        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: "acme", IsRootPath: false, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));
        Assert.Null(match);

        // Triggering a snapshot build (e.g., a host-mode lookup) runs the builder and the
        // warning fires.
        await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: true, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.Contains(captured, e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("acme-corp")
            && e.Message.Contains("WebRouting:Path 'acme'")
            && e.Message.Contains("differs from the blueprint name"));
    }

    [Fact(DisplayName = "Path with leading slash is rejected at index-population time, never throws on hot path")]
    public async Task LeadingSlashPath_ExcludedQuietly()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("acme", b => b.WithConfiguration("WebRouting:Path", "/acme"));

        var index = new DefaultShellRouteIndex(provider);

        // Path-by-name lookup invokes the builder which rejects the leading slash.
        var match = await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: "acme", IsRootPath: false, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.Null(match);
    }

    [Fact(DisplayName = "ContainsShellName returns false before any snapshot is built")]
    public void ContainsShellName_NoSnapshot_ReturnsFalse()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""));

        var index = new DefaultShellRouteIndex(provider);

        Assert.False(index.ContainsShellName("Default"));
        Assert.False(index.ContainsShellName("acme"));
        // No snapshot should have been built as a side effect of this query.
        Assert.Equal(0, provider.ListCount);
    }

    [Fact(DisplayName = "ContainsShellName matches case-insensitively against the current snapshot")]
    public async Task ContainsShellName_MatchesCaseInsensitively()
    {
        var provider = new StubShellBlueprintProvider()
            .Add("Default", b => b.WithConfiguration("WebRouting:Path", ""));

        var index = new DefaultShellRouteIndex(provider);

        // Trigger a snapshot rebuild by issuing a root-path lookup.
        await index.TryMatchAsync(new ShellRouteCriteria(
            PathFirstSegment: null, IsRootPath: true, Host: null,
            HeaderName: null, HeaderValue: null, ClaimKey: null, ClaimValue: null));

        Assert.True(index.ContainsShellName("Default"));
        Assert.True(index.ContainsShellName("default"));
        Assert.True(index.ContainsShellName("DEFAULT"));
        Assert.False(index.ContainsShellName("acme"));
    }

    private sealed class CapturingLogger<T>(List<(LogLevel Level, string Message)> sink) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
