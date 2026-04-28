using System.Collections.Frozen;
using System.Collections.Immutable;

namespace CShells.AspNetCore.Routing;

/// <summary>
/// Immutable indexed view of the route index. Published by <see cref="DefaultShellRouteIndex"/>
/// via volatile reference swap so concurrent reads always observe a fully-consistent state.
/// </summary>
internal sealed class ShellRouteIndexSnapshot
{
    public required FrozenDictionary<string, ShellRouteEntry> ByPathSegment { get; init; }
    public required FrozenDictionary<string, ShellRouteEntry> ByHost { get; init; }
    public required FrozenDictionary<string, ShellRouteEntry> ByHeaderValue { get; init; }
    public required FrozenDictionary<string, ShellRouteEntry> ByClaimValue { get; init; }
    public ShellRouteEntry? RootPathEntry { get; init; }
    public bool RootPathAmbiguous { get; init; }
    public required ImmutableArray<ShellRouteEntry> All { get; init; }

    public static ShellRouteIndexSnapshot Empty { get; } = new()
    {
        ByPathSegment = FrozenDictionary<string, ShellRouteEntry>.Empty,
        ByHost = FrozenDictionary<string, ShellRouteEntry>.Empty,
        ByHeaderValue = FrozenDictionary<string, ShellRouteEntry>.Empty,
        ByClaimValue = FrozenDictionary<string, ShellRouteEntry>.Empty,
        RootPathEntry = null,
        RootPathAmbiguous = false,
        All = [],
    };
}
