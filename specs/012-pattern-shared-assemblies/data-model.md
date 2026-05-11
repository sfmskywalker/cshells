# Data Model: Pattern-Based Shared Assemblies

## Shared Assembly Selector

Represents one host-wide rule that can select candidate assemblies for sharing.

**Fields**:

- `Kind`: `Exact`, `PrefixPattern`, or `Predicate`.
- `Pattern`: Original string for exact and prefix-pattern selectors; null for predicates.
- `Source`: Human-readable source, such as `CShells:SharedAssemblies:1` or `CShellsBuilder.WithSharedAssembliesWhere`.
- `Predicate`: Code-first delegate for predicate selectors only.

**Validation Rules**:

- String selectors must be non-null, non-empty, and not whitespace-only.
- `*` is valid only as the final character.
- Entries without `*` are exact selectors.
- Entries ending in `*` are prefix-pattern selectors.
- Predicate selectors must be non-null.
- Matching uses assembly simple names only with `StringComparer.OrdinalIgnoreCase`.

## Assembly Simple Name

The short assembly identity used for matching.

**Fields**:

- `Name`: `AssemblyName.Name` or equivalent simple name.

**Validation Rules**:

- Empty or null candidate names are ignored for matching.
- Full names, versions, cultures, public key tokens, file names, and paths are never used for selector matching.

## Shared Assembly Match

Represents a positive decision that one candidate assembly is shared.

**Fields**:

- `AssemblyName`: Candidate simple name.
- `SelectorKind`: The selector kind responsible for the match.
- `SelectorPattern`: Exact name or prefix pattern when available.
- `SelectorSource`: Configuration path or code-first source responsible for the match.

**Relationships**:

- A match references one selected assembly simple name.
- A match references the first selector source responsible for the selected decision.
- Multiple selectors may match the same assembly, but the final shared decision is emitted once.

**Validation Rules**:

- Deduplicate matches by assembly simple name case-insensitively.
- Duplicate selectors across configuration and code-first setup must not create duplicate shared decisions or duplicate diagnostics.

## Shared Assembly Configuration Source

Represents the host-wide origin for selectors.

**Fields**:

- `ConfigurationPath`: Root configuration path for string selectors, e.g. `CShells:SharedAssemblies:0`.
- `BuilderSource`: Public API source for code-first selectors, e.g. `WithSharedAssemblies("Elsa.*")`.

**Validation Rules**:

- Invalid configured entries must report the configuration path.
- Invalid code-first entries must report the API source or argument name.

## State Transitions

Selectors are immutable after host configuration composition:

```text
Declared -> Validated/Compiled -> Applied To Candidate Assemblies -> Match Diagnostics Available
```

Invalid selectors stop at `Declared` and fail before shell activation.
