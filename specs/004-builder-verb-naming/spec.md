# Feature Specification: Fluent Builder Naming Matrix

**Feature Branch**: `004-builder-verb-naming`  
**Created**: 2026-04-12  
**Status**: Draft  
**Input**: User description: "Update the existing 004 feature so it remains the naming decision record for the fluent builder matrix, but also covers implementing and protecting that approved naming scheme in the repository. Keep `From*` for source selection, `With*` for provider attachment, preserve `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`, and keep the implementation scope minimal and grounded in the current repository reality."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preserve the Approved Public Naming Surface (Priority: P1)

As a CShells maintainer, I want the approved fluent builder naming matrix applied to the shipped assembly-discovery API so the public surface keeps the intended `From*` versus `With*` meaning without reopening already approved names.

**Why this priority**: The main product value is no longer just documenting the naming rule. The repository must actually keep the approved names in the codebase so downstream work does not drift or re-litigate the decision.

**Independent Test**: Inspect the repository's public assembly-discovery builder entry points and verify that source-selection calls use `From*`, provider-attachment calls use `With*`, and the approved names remain present without introducing replacement names for the same responsibilities.

**Acceptance Scenarios**:

1. **Given** a fluent builder method that selects where feature discovery gets assemblies from, **When** the assembly-discovery API is reviewed, **Then** that public entry point uses the `From*` verb family.
2. **Given** a fluent builder method that attaches a provider instance, factory, or provider type for discovery extensibility, **When** the public API is reviewed, **Then** that public entry point uses the `With*` verb family.
3. **Given** the approved assembly-discovery names `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`, **When** feature `004-builder-verb-naming` is implemented, **Then** those names remain the approved and shipped naming surface for this repository.
4. **Given** the repository already conforms to the approved names, **When** this feature is implemented, **Then** the work does not invent unrelated renames or replacement verbs.

---

### User Story 2 - Guard Against Naming Regression (Priority: P2)

As a CShells maintainer, I want implementation guardrails around the assembly-discovery naming surface so future changes fail review or validation if they drift away from the approved verb-family scheme.

**Why this priority**: A naming matrix only stays effective if the repository has a durable way to detect accidental renames, stale aliases, or newly introduced conflicting verbs.

**Independent Test**: Run the repository verification added for this feature and confirm it passes when the approved naming surface is intact and would fail if an approved entry point were removed, renamed, or replaced with an unapproved alternative.

**Acceptance Scenarios**:

1. **Given** the approved assembly-discovery naming surface, **When** repository validation runs, **Then** there is automated or equivalently enforceable verification that the approved names are still present.
2. **Given** a future change that introduces `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, or `AddHostAssemblies` as replacement public names for the same responsibilities, **When** the guardrails are evaluated, **Then** the change is detected as violating feature `004-builder-verb-naming`.
3. **Given** `WithAssemblyProvider(...)` includes more than one supported way to attach a provider, **When** the guardrails are evaluated, **Then** they protect the approved naming surface without treating valid overload variations as naming drift.

---

### User Story 3 - Keep Developer-Facing Guidance Consistent (Priority: P3)

As a CShells developer or maintainer, I want samples, docs, and explanatory comments aligned with the approved naming direction so usage guidance matches the public API and reinforces the same builder vocabulary.

**Why this priority**: Even when code already uses the correct names, stale documentation or comments can reintroduce confusion and encourage regressions in future contributions.

**Independent Test**: Review the in-scope developer-facing guidance for assembly discovery and verify it consistently uses the approved names and verb-family rationale, with any stale or conflicting terminology removed or corrected.

**Acceptance Scenarios**:

1. **Given** developer-facing samples or documentation that describe assembly discovery configuration, **When** they are reviewed for this feature, **Then** they use `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` consistently.
2. **Given** comments or inline guidance that explain the builder vocabulary, **When** they mention source selection versus provider attachment, **Then** they reflect the approved `From*` and `With*` matrix.
3. **Given** the prior-art decision from `specs/003-fluent-assembly-selection`, **When** this feature is reviewed, **Then** the implementation-backed scope remains aligned with that prior decision while enabling real repository changes in code, tests, samples, and documentation where needed.

### Edge Cases

- The repository already uses the approved public method names; implementation must still produce value through guardrails or guidance cleanup rather than forcing renames for the sake of change.
- A future contributor adds an alias with the wrong verb family while leaving the approved method in place; the feature must treat that as naming drift, not as harmless duplication.
- Multiple overloads or generic forms exist under `WithAssemblyProvider(...)`; verification must protect the approved naming family without requiring only one attachment shape.
- A sample or comment uses an unapproved verb even though the runtime API is correct; the feature must treat that as an in-scope consistency defect.
- A future builder API outside assembly discovery borrows the matrix; the recorded rationale must still distinguish source selection from provider attachment without requiring unrelated renames in this feature.

## Approved Naming Matrix

| Builder responsibility | Approved verb family | Rationale |
| --- | --- | --- |
| Select where feature discovery gets assemblies from | `From*` | The method describes the source of discovery input, not the attachment of an extensibility component. |
| Attach a provider instance, factory, or provider type that contributes assemblies | `With*` | The method adds an extension component to the builder and should read as provider attachment. |

## Candidate Evaluation Record

| Candidate | Outcome | Reason |
| --- | --- | --- |
| `FromAssemblies(...)` | Approved | It clearly communicates explicit assembly source selection and matches the approved `From*` family for discovery inputs. |
| `FromHostAssemblies()` | Approved | It clearly communicates selection of the host-derived assembly source and preserves the approved source-selection verb family. |
| `WithAssemblyProvider(...)` | Approved | It clearly communicates attachment of a provider extension point and matches the approved `With*` family for provider inputs. |
| `WithAssemblies(...)` | Rejected | It blurs source selection with attachment language and suggests assemblies are being attached as components rather than selected as discovery input. |
| `WithHostAssemblies()` | Rejected | It uses provider-attachment wording for a source-selection action and weakens the distinction between host-derived sources and provider extensions. |
| `AddAssemblies(...)` | Rejected | It sounds like raw collection mutation instead of a fluent source-selection decision and does not reinforce the approved builder vocabulary. |
| `AddHostAssemblies()` | Rejected | It suggests additive mutation rather than an explicit source-selection directive and does not preserve the approved naming matrix. |

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Feature `004-builder-verb-naming` MUST define the fluent builder naming matrix that distinguishes source-selection verbs from provider-attachment verbs.
- **FR-002**: The naming matrix MUST state that methods describing where discovery gets assemblies from belong to the `From*` verb family.
- **FR-003**: The naming matrix MUST state that methods attaching provider instances, factories, or provider types belong to the `With*` verb family.
- **FR-004**: The feature MUST explicitly evaluate the candidates `FromAssemblies`, `FromHostAssemblies`, `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, `AddHostAssemblies`, and `WithAssemblyProvider`.
- **FR-005**: The candidate evaluation MUST record an approved or rejected outcome and rationale for each candidate.
- **FR-006**: The approved assembly-discovery names for this feature MUST remain `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`.
- **FR-007**: The repository implementation scope for this feature MUST keep the existing approved public API names in code rather than introducing alternative public names for the same responsibilities.
- **FR-008**: If the repository already conforms to the approved names, this feature MUST allow implementation to conclude without public API renames and instead focus on protection and alignment work.
- **FR-009**: The implementation for this feature MUST add guardrails that verify the approved public naming surface and detect regression if an unapproved replacement or conflicting name is introduced.
- **FR-010**: The guardrails MUST protect the approved naming surface for source-selection and provider-attachment entry points without flagging legitimate overloads or equivalent attachment forms that retain the approved names.
- **FR-011**: The implementation for this feature MUST align in-scope developer-facing samples, documentation, and explanatory comments with the approved naming matrix wherever those assets describe the assembly-discovery builder surface.
- **FR-012**: The feature MUST keep scope minimal and grounded in current repository reality by avoiding unrelated builder renames outside the approved assembly-discovery naming surface.
- **FR-013**: The feature MUST remain aligned with the prior approved naming direction from `specs/003-fluent-assembly-selection` while converting feature `004-builder-verb-naming` into an implementation-backed feature that permits real code, test, sample, documentation, and comment updates.

### Key Entities *(include if feature involves data)*

- **Fluent Builder Naming Matrix**: The verb-family decision table that maps builder responsibilities to `From*` or `With*` naming.
- **Approved Naming Surface**: The shipped public assembly-discovery entry points that must remain `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`.
- **Candidate Name Evaluation**: A recorded decision for one candidate name, including its verb family, approved or rejected status, and rationale.
- **Naming Guardrail**: Repository validation that protects the approved naming surface against regression.
- **Developer-Facing Guidance Asset**: Any sample, documentation, or explanatory comment that teaches or reinforces the assembly-discovery builder vocabulary.
- **Prior-Art Feature Context**: The existing `003-fluent-assembly-selection` decision record that established the approved assembly-discovery names.

### Assumptions

- The current repository already appears to expose `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`, so the most likely implementation work is guardrails plus targeted guidance cleanup rather than API renaming.
- CShells builder vocabulary continues to treat `From*` as source selection and `With*` as provider attachment, and this feature does not reopen that decision.
- Multiple supported attachment shapes may exist under the approved `WithAssemblyProvider(...)` name, and they should continue to count as one approved naming family.
- The feature is intentionally limited to the assembly-discovery builder surface and the developer-facing assets that describe it.
- Prior-art in `specs/003-fluent-assembly-selection` remains authoritative context for the approved names, while feature `004-builder-verb-naming` now governs implementing and protecting that decision in the repository.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of public assembly-discovery builder entry points covered by this feature use the approved naming surface: `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`.
- **SC-002**: Repository validation includes at least one guardrail that passes when the approved naming surface is intact and fails when an unapproved replacement name is introduced for an in-scope responsibility.
- **SC-003**: 100% of in-scope developer-facing assembly-discovery examples and guidance reviewed for this feature use the approved naming matrix consistently.
- **SC-004**: The implementation for this feature can be completed without public API renames when the repository already conforms, with change scope limited to the guardrails and alignment work needed to protect the approved naming direction.
