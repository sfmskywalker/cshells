using System.Collections.Immutable;

namespace CShells.AspNetCore.Routing;

/// <summary>
/// Bounded view of the route entries the index currently knows about, returned by
/// <see cref="IShellRouteIndex.GetCandidateSnapshot"/>. <see cref="Entries"/> is capped at
/// the caller-supplied <c>maxEntries</c>; <see cref="Total"/> reports the full count so the
/// caller can render an accurate "(+N more)" suffix on the no-match diagnostic line.
/// </summary>
/// <param name="Entries">The capped slice of entries (at most <c>maxEntries</c>).</param>
/// <param name="Total">
/// Total number of entries in the underlying snapshot, regardless of the cap. Equals
/// <c>Entries.Length</c> when nothing was truncated.
/// </param>
public sealed record ShellRouteCandidateSnapshot(ImmutableArray<ShellRouteEntry> Entries, int Total);
