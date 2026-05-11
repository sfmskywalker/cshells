# Feature Specification: Pattern-Based Shared Assemblies

**Feature Branch**: `012-pattern-shared-assemblies`  
**Created**: 2026-05-11  
**Status**: Draft  
**Input**: User description: "Add pattern-based shared assembly selection for CShells. Users should be able to specify shared assembly simple-name patterns from configuration such as appsettings.json, using wildcard patterns like Elsa.* to reduce verbose lists for framework ecosystems. The feature should also support code-first selection, including a predicate-style WithSharedAssembliesWhere experience. Matching is against assembly simple names only. Pattern support should be explicit and bounded so framework contracts/common infrastructure can be shared without accidentally sharing tenant-specific implementation assemblies."

## Clarifications

### Session 2026-05-11

- Q: What wildcard grammar should shared assembly patterns support? → A: `*` is allowed only as the final character, so patterns are exact names or prefix patterns like `Elsa.*`.
- Q: Should shared assembly selectors be host-wide or per-shell? → A: Selectors are host-wide under the root `CShells` configuration and code-first builder APIs.
- Q: Should exact names and wildcard patterns use one collection or separate configuration/API collections? → A: Use one unified host-wide `SharedAssemblies` collection for exact names and prefix wildcard patterns.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure Shared Assemblies With Patterns (Priority: P1)

As a CShells host author, I want to declare shared assembly patterns in configuration so framework ecosystems with many related packages can be shared without listing every assembly one by one.

**Why this priority**: Configuration-driven setup is the main usability problem. It lets hosts and deployment environments express shared framework families consistently without recompiling application code.

**Independent Test**: Can be fully tested by loading root `CShells:SharedAssemblies` configuration that declares exact and wildcard shared assembly simple-name patterns in one collection, then confirming matching assemblies are treated as shared and non-matching assemblies are not.

**Acceptance Scenarios**:

1. **Given** root `CShells:SharedAssemblies` configuration declares entries `Elsa` and `Elsa.*`, **When** assemblies named `Elsa`, `Elsa.Workflows`, and `Contoso.Workflows` are evaluated, **Then** only `Elsa` and `Elsa.Workflows` match the shared assembly configuration.
2. **Given** configuration declares an exact shared assembly name without a wildcard, **When** an assembly with a longer name that begins with the same text is evaluated, **Then** the longer name does not match.
3. **Given** configuration declares multiple shared assembly patterns, **When** an assembly matches any one pattern, **Then** it is considered shared exactly once.

---

### User Story 2 - Configure Shared Assemblies In Code (Priority: P2)

As a library integrator, I want a code-first way to select shared assemblies, including predicate-based matching, so reusable integration packages can provide sensible defaults for framework-specific shared assemblies.

**Why this priority**: Framework integration packages need an ergonomic way to register shared assembly conventions without requiring every consuming application to repeat the same configuration.

**Independent Test**: Can be fully tested by configuring shared assembly matching in code and confirming the resulting shared assembly decisions are the same as equivalent configuration-driven patterns.

**Acceptance Scenarios**:

1. **Given** a code-first shared assembly pattern is registered for `Elsa.*`, **When** assemblies named `Elsa.Workflows` and `Other.Workflows` are evaluated, **Then** only `Elsa.Workflows` matches.
2. **Given** a code-first predicate-based selector is registered, **When** assemblies are evaluated, **Then** the predicate can include or exclude assemblies based on their simple names.
3. **Given** configuration-driven and code-first shared assembly selectors are both present, **When** assemblies are evaluated, **Then** an assembly matching either source is considered shared without producing duplicate shared entries.

---

### User Story 3 - Keep Matching Explicit And Bounded (Priority: P3)

As an application maintainer, I want shared assembly matching to be limited to assembly simple names so wildcard use is predictable and does not accidentally match file paths, versions, cultures, or tenant-specific implementation details.

**Why this priority**: Shared assemblies affect isolation boundaries. Predictable matching reduces the risk of unintentionally sharing runtime implementation assemblies that should remain shell-specific.

**Independent Test**: Can be fully tested by evaluating assemblies whose simple names, full names, versions, cultures, and file paths differ and confirming only simple-name patterns affect the result.

**Acceptance Scenarios**:

1. **Given** a pattern `Elsa.*`, **When** an assembly full name includes version and culture metadata, **Then** the match decision uses only the simple name portion.
2. **Given** an assembly file path contains a matching folder or file segment but the simple assembly name does not match, **When** the assembly is evaluated, **Then** it is not considered shared.
3. **Given** a pattern is overly broad, **When** the host starts, **Then** users can identify the configured pattern responsible for the shared match during diagnostics or troubleshooting.

---

### User Story 4 - Document Framework-Friendly Usage (Priority: P4)

As a developer adopting CShells with a framework such as Elsa, I want examples and guidance that show when to use exact names, wildcard patterns, and predicate-based code selection so I can reduce verbosity without weakening shell isolation.

**Why this priority**: The feature adds a powerful shortcut. Documentation must set expectations around safe use, especially around framework contracts and common infrastructure versus shell-specific implementations.

**Independent Test**: Can be fully tested by reviewing documentation and samples and confirming they show configuration-driven patterns, code-first patterns, predicate-based selection, and cautions about broad sharing.

**Acceptance Scenarios**:

1. **Given** a developer reads the configuration documentation, **When** they need to share a framework family such as Elsa, **Then** they can find an example using exact and wildcard simple-name patterns.
2. **Given** a framework integration author reads the code-first documentation, **When** they need custom selection logic, **Then** they can find an example of predicate-based shared assembly selection.
3. **Given** a developer reads the guidance, **When** they consider sharing implementation assemblies, **Then** the documentation explains the isolation tradeoff and recommends narrow patterns.

### Edge Cases

- Blank, whitespace-only, or otherwise empty shared assembly pattern entries must be rejected with a clear configuration error.
- Duplicate patterns across configuration and code-first setup must not produce duplicate shared assembly entries or duplicate diagnostics.
- Exact names must not behave as prefix matches unless the pattern explicitly contains a wildcard.
- Exact names and prefix wildcard patterns must be declared through one unified host-wide shared assembly collection.
- Wildcard patterns may only use `*` as the final character; patterns with `*` in any other position must be rejected as invalid.
- Matching must be case-insensitive to align with common assembly-name comparison expectations.
- Wildcards must match only simple assembly names, not assembly full names, versions, cultures, public key tokens, file names, or directory paths.
- Patterns that match no available assemblies are valid but should remain visible in diagnostics so users can find stale or misspelled configuration.
- Assemblies that are already loaded or contributed by multiple sources must resolve to one shared assembly decision.
- Shared assembly selectors are host-wide and must not be scoped differently per shell.
- Predicate-based selection that throws or cannot evaluate an assembly must fail with actionable feedback that identifies the selector source.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow shared assemblies to be selected by exact assembly simple names.
- **FR-002**: The system MUST allow shared assemblies to be selected by prefix wildcard patterns applied to assembly simple names, with `*` allowed only as the final character.
- **FR-003**: The system MUST support declaring host-wide exact names and prefix wildcard patterns through one root `CShells:SharedAssemblies` application configuration collection.
- **FR-004**: The system MUST support declaring host-wide exact names and prefix wildcard patterns through one code-first builder collection.
- **FR-005**: The system MUST provide a code-first predicate-based shared assembly selector, including a user-facing `WithSharedAssembliesWhere` experience.
- **FR-006**: The system MUST evaluate shared assembly pattern matches against assembly simple names only.
- **FR-007**: The system MUST NOT evaluate shared assembly patterns against assembly full names, file paths, versions, cultures, or public key tokens.
- **FR-008**: The system MUST treat exact shared assembly names and wildcard shared assembly patterns as additive sources.
- **FR-009**: The system MUST deduplicate shared assembly matches when an assembly is selected by multiple exact names, patterns, predicates, or configuration sources.
- **FR-010**: The system MUST compare shared assembly simple names case-insensitively.
- **FR-011**: The system MUST reject blank or whitespace-only shared assembly pattern entries before shell activation.
- **FR-012**: The system MUST provide clear feedback when a configured shared assembly pattern is invalid, including the affected configuration path or source.
- **FR-013**: The system MUST make shared assembly match decisions inspectable enough for users to troubleshoot which exact name, wildcard pattern, or predicate selected an assembly.
- **FR-014**: The system MUST keep existing explicit shared assembly declarations supported alongside pattern-based declarations.
- **FR-015**: Documentation MUST show configuration examples for exact and wildcard shared assembly simple-name patterns.
- **FR-016**: Documentation MUST show code-first examples for exact names, wildcard patterns, and predicate-based shared assembly selection.
- **FR-017**: Documentation MUST explain that broad wildcard patterns can weaken shell isolation and should normally target framework contracts or common infrastructure packages.

### Key Entities *(include if feature involves data)*

- **Shared Assembly Selector**: A user-declared rule that identifies assemblies to share across shells.
- **Assembly Simple Name**: The short assembly identity used for matching, excluding version, culture, public key token, file path, and other metadata.
- **Exact Shared Assembly Name**: A selector that matches one assembly simple name exactly.
- **Wildcard Shared Assembly Pattern**: A selector ending in `*` that may match multiple assembly simple names by prefix.
- **Predicate Shared Assembly Selector**: A code-first selector that decides whether an assembly simple name should be shared.
- **Shared Assembly Match**: The resulting decision that an assembly is shared, including the selector source responsible for that decision.
- **Shared Assembly Configuration Source**: The root `CShells:SharedAssemblies` configuration location or code-first builder registration that contributes exact names, wildcard patterns, or predicate selectors.

### Assumptions

- Wildcard matching is intentionally limited to assembly simple names.
- `*` is the only required wildcard character for the initial feature and is valid only as the final character in a pattern.
- Matching is case-insensitive.
- Existing exact shared assembly configuration, if present, remains valid and composes with pattern-based selection.
- Exact names and prefix wildcard patterns share one host-wide `SharedAssemblies` collection; entries without `*` are exact names and entries ending in `*` are prefix patterns.
- Shared assembly selectors apply at the host level before per-shell activation and are not overridden per shell.
- Pattern entries that match no assemblies are allowed because optional framework packages may not always be referenced by every host.
- Configuration examples use `Elsa` and `Elsa.*` as representative framework-family patterns.
- Predicate-based selection is a code-first capability only; configuration remains string-pattern based.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can replace a list of at least five related framework assembly names with two configuration entries, such as one exact name and one wildcard pattern, while selecting the same matching assemblies.
- **SC-002**: 100% of shared assembly matching tests confirm that exact names match only identical simple names.
- **SC-003**: 100% of shared assembly matching tests confirm that wildcard patterns match simple names only and do not use full names, versions, cultures, public key tokens, file paths, or directory paths.
- **SC-004**: 100% of invalid blank or whitespace-only pattern entries fail before shell activation with actionable feedback.
- **SC-005**: Shared assembly selectors contributed by configuration and code-first setup produce one deduplicated shared decision per assembly.
- **SC-006**: A predicate-based code-first selector can include and exclude assemblies by simple name in verified tests.
- **SC-007**: Documentation and samples include at least one configuration-driven wildcard example, one exact-name example, one code-first predicate example, and one isolation guidance note.
