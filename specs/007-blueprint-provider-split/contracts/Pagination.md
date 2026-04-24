# Contract: Pagination DTOs and Composite Cursor Codec

**Feature**: [007-blueprint-provider-split](../spec.md)
**Location**: `CShells.Abstractions/Lifecycle/`

## Blueprint pagination

### `BlueprintListQuery`

```csharp
public sealed record BlueprintListQuery(
    string? Cursor = null,
    int Limit = 50,
    string? NamePrefix = null);
```

Validation (enforced by `Guard.Against.*` at the provider's public entry):
- `Limit` range: `[1, 500]`. Values outside the range throw
  `ArgumentOutOfRangeException`.
- `NamePrefix` is treated as case-insensitive ordinal.
- `Cursor = null` requests the first page. Non-null cursors are opaque and are returned
  verbatim by a prior `ListAsync` call on the same provider (or composite).

### `BlueprintPage`

```csharp
public sealed record BlueprintPage(
    IReadOnlyList<BlueprintSummary> Items,
    string? NextCursor);
```

Invariants:
- `Items.Count <= query.Limit`.
- `NextCursor = null` iff no further pages exist.

### `BlueprintSummary`

```csharp
public sealed record BlueprintSummary(
    string Name,
    string SourceId,
    bool Mutable,
    IReadOnlyDictionary<string, string> Metadata);
```

- `Name` is non-empty.
- `SourceId` is a stable identifier for the provider that owns this blueprint. Providers
  typically use their own type's short name. Callers treat `SourceId` as opaque.
- `Mutable == true` iff the owning provider exposes a manager for this name.
- `Metadata` is provider-defined free-form string/string pairs; may be empty.

## Shell pagination

### `ShellListQuery`

```csharp
public sealed record ShellListQuery(
    string? Cursor = null,
    int Limit = 50,
    string? NamePrefix = null,
    ShellLifecycleState? StateFilter = null);
```

Same validation as `BlueprintListQuery`, plus:
- `StateFilter`: when set, only entries with an active shell in the given state are
  returned. Entries with no active shell are filtered out entirely; entries in
  **different** states are also filtered out.

### `ShellPage`

```csharp
public sealed record ShellPage(
    IReadOnlyList<ShellSummary> Items,
    string? NextCursor);
```

### `ShellSummary`

```csharp
public sealed record ShellSummary(
    string Name,
    string SourceId,
    bool Mutable,
    int? ActiveGeneration,
    ShellLifecycleState? State,
    int ActiveScopeCount,
    DateTimeOffset? LastScopeOpenedAt,
    IReadOnlyDictionary<string, string> Metadata);
```

Invariants:
- When there is no active shell: `ActiveGeneration`, `State`, `LastScopeOpenedAt` are
  all null; `ActiveScopeCount` is `0`.
- When there is an active shell: all four fields reflect the state at page-assembly
  time. Subsequent mutations do not retroactively update the returned page.

## Composite cursor codec

The composite provider multiplexes multiple sub-providers. Its cursor format is
deterministic and self-describing.

**Encoding**: Base64-UTF8 of a compact JSON document:

```json
{ "v": 1, "entries": [ { "p": 0, "c": "<sub-cursor>" }, { "p": 2, "c": "<sub-cursor>" } ] }
```

- `v`: integer codec version. Current: `1`. Changes MUST be documented and handled by
  the composite's decoder.
- `entries`: array of records, one per sub-provider with remaining work. Order is
  preserved across pages (always the DI-registration order of remaining providers).
- `p`: sub-provider index in the composite's DI-registration order (0-based).
- `c`: sub-provider's own cursor string, treated as opaque by the composite.

**Null cursor conventions**:
- The first page is requested with `query.Cursor = null`; the composite seeds each
  sub-provider with `null` in turn.
- When every sub-provider has exhausted its catalogue (each sub-`NextCursor` is null),
  the composite emits `NextCursor = null`.
- A composite cursor with an empty `entries` array is never produced — the composite
  normalizes to `null` in that case.

**Version-mismatch handling**: a decoded cursor whose `v` is unknown raises
`InvalidOperationException("Composite cursor version {v} is not supported by this host.")`.
This only occurs if a cursor survives across a host upgrade that bumped the codec
version — callers are expected to restart pagination from `null`.

## Testing surface

- `CompositeCursorCodecTests`: round-trip encoding, version-mismatch rejection, empty-
  entries normalization, corrupted-base64 handling.
- `CompositeShellBlueprintProviderTests`: integration coverage of composite paging
  across two and three sub-providers, including the case where a sub-provider exhausts
  mid-page and the composite fills the remainder from the next sub-provider.
