# Research: Map-Based Shell Configuration

## Decision 1: Read shells as named configuration children

**Decision**: Treat `CShells:Shells` as an object map. Each immediate child section is one shell, and the child section key is the shell name.

**Rationale**: This matches how the .NET configuration system merges hierarchical keys: later providers override or extend the same path by key name. A named shell path such as `CShells:Shells:Default` is stable across file reordering and has a clear environment variable equivalent.

**Alternatives considered**:

- Continue binding `CShells:Shells` to a list: rejected because array indices merge by position and create opaque override paths.
- Support both array and map shapes: rejected because the specification explicitly removes backward compatibility and the constitution favors simpler breaking changes when they improve API quality.

## Decision 2: Shell identity comes only from the map key

**Decision**: Populate runtime shell names, blueprint names, summaries, descriptors, and registry-visible names from the `CShells:Shells` child key only.

**Rationale**: A single source of identity prevents drift between the configuration path and the runtime shell name. It also makes environment overrides self-documenting because the shell identifier appears in the path.

**Alternatives considered**:

- Allow an inner `Name` property to override the key: rejected because it reintroduces duplicate identity and weakens merge-by-name semantics.
- Treat inner `Name` as a required field: rejected because the target format intentionally drops it.

## Decision 3: Reject numeric shell child keys as unsupported array syntax

**Decision**: If an immediate child under `CShells:Shells` has a numeric key, fail with an actionable error that identifies the path and explains that shell entries must be named.

**Rationale**: Configuration arrays appear as numeric child keys. Rejecting them early prevents accidental activation from unsupported legacy configuration and gives users a direct migration clue.

**Alternatives considered**:

- Silently skip numeric children: rejected because silent failures violate explicit error handling and can hide missing shells.
- Interpret numeric keys as shell names: rejected because it would make array syntax appear to work while creating invalid shell names like `0`.

## Decision 4: Preserve existing feature map behavior

**Decision**: Do not change feature parsing. A shell definition's `Features` node continues to use the existing feature object-map behavior, including treating a feature entry's inner `Name` as feature configuration rather than feature identity in object-map syntax.

**Rationale**: The shell format change intentionally mirrors the already-supported feature map shape. Reworking feature parsing would expand scope and risk regressions unrelated to shell identity.

**Alternatives considered**:

- Remove legacy feature array support at the same time: rejected as unrelated scope; the feature specification only removes shell array support.
- Add a new feature configuration shape: rejected because the existing map syntax already satisfies the need.

## Decision 5: Keep direct-key lookup as the provider fast path

**Decision**: `ConfigurationShellBlueprintProvider.GetAsync(name)` should continue to use direct child lookup for normal named shells and should remove the fallback scan for inner `Name` overrides.

**Rationale**: Map syntax makes direct lookup correct by construction and avoids O(N) scans for normal requests. Removing the fallback also enforces the single-source-of-truth rule for shell identity.

**Alternatives considered**:

- Keep fallback scanning for compatibility: rejected because array/name compatibility is explicitly out of scope.
- Always enumerate children: rejected because it is unnecessary and less scalable than direct lookup.

## Decision 6: Document casing conventions separately for config and environment variables

**Decision**: Recommend PascalCase shell names in JSON/configuration files and show the same shell-name segment in environment variable paths, allowing casing differences but not added underscores, for example `CSHELLS__SHELLS__MYSHELL__...`.

**Rationale**: Shell names are user-visible in configuration and management surfaces, while environment variables often follow uppercase conventions. Documenting both avoids ambiguity without adding custom normalization rules.

**Alternatives considered**:

- Require uppercase shell names everywhere: rejected because it degrades readability in JSON and runtime display names.
- Implement custom environment variable name normalization: rejected because it would diverge from the existing configuration provider behavior.

## Decision 7: Update configuration model serialization around shell maps

**Decision**: Change root shell configuration models that represent `CShells:Shells` from list-shaped storage to map-shaped storage, while keeping per-shell `ShellConfig` focused on shell contents.

**Rationale**: Direct JSON deserialization or serialization of the root configuration model should reflect the supported external contract. Keeping `Name` out of per-shell examples and model expectations reinforces map-key identity.

**Alternatives considered**:

- Keep list-shaped model but convert at provider boundaries: rejected because direct model usage would still expose unsupported array syntax.
- Add separate legacy and map model types: rejected as unnecessary compatibility complexity.
