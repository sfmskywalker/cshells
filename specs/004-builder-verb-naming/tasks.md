# Tasks: Fluent Builder Naming Matrix

**Input**: Design documents from `/specs/004-builder-verb-naming/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `quickstart.md`, `contracts/builder-naming-matrix.md`

**Tests**: Focused xUnit guardrails and targeted `dotnet test`/`dotnet build` validation are required for this feature because the implementation scope now includes protecting the shipped assembly-discovery API surface.

**Organization**: Tasks are grouped by user story so the approved naming surface can be preserved in code first, then protected with regression tests, then reflected in the small set of in-scope guidance assets.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., `US1`, `US2`, `US3`)
- Include exact file paths in descriptions

## Path Conventions

- Repository root: `/Users/sipke/Projects/ValenceWorks/cshells/main`
- Runtime implementation stays limited to `src/CShells/` and code-adjacent XML comments in `src/CShells.AspNetCore/`
- Regression guardrails stay inside `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/`
- Guidance alignment stays limited to the already relevant README/doc/wiki/sample assets that describe assembly discovery

## Phase 1: Setup (Scope Lock)

**Purpose**: Confirm the current repository baseline and keep the execution scope minimal before changing code, tests, or guidance.

- [X] T001 [P] Audit the current assembly-discovery public surface in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs` and the current coverage baseline in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs`
- [X] T002 [P] Audit the in-scope assembly-discovery guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/README.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/getting-started.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/multiple-shell-providers.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/README.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ShellExtensions.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Getting-Started.md`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Capture the targeted verification baseline so later validation stays focused on the approved naming surface and in-scope assets only.

**⚠️ CRITICAL**: No user story work should begin until this phase is complete.

- [X] T003 Baseline targeted verification against `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/CShells.Workbench.csproj` so the implementation stays minimal and implementation-backed

**Checkpoint**: The exact code, test, guidance, and validation touchpoints are locked.

---

## Phase 3: User Story 1 - Preserve the Approved Public Naming Surface (Priority: P1) 🎯 MVP

**Goal**: Keep the shipped assembly-discovery API fixed to `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` while reinforcing the approved `From*` versus `With*` meaning in code-adjacent guidance.

**Independent Test**: Inspect `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ShellExtensions.cs` and confirm the only in-scope public assembly-discovery names are `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`, with no competing aliases or conflicting verb-family wording.

### Implementation for User Story 1

- [X] T004 [US1] Update `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs` only as needed to keep the assembly-discovery public surface fixed to `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` without introducing unrelated renames or aliases
- [X] T005 [P] [US1] Align the assembly-discovery remarks in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs` with the approved `From*` source-selection and `With*` provider-attachment terminology
- [X] T006 [P] [US1] Align the assembly-discovery remarks in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/Extensions/ShellExtensions.cs` with the approved `From*` source-selection and `With*` provider-attachment terminology

**Checkpoint**: The code surface and code-adjacent comments preserve the approved naming matrix.

---

## Phase 4: User Story 2 - Guard Against Naming Regression (Priority: P2)

**Goal**: Add focused repository-native guardrails in the existing xUnit project so future naming drift is caught automatically.

**Independent Test**: Run targeted xUnit validation and confirm it passes only when `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/DependencyInjection/CShellsBuilderExtensions.cs` still exposes `FromAssemblies(...)`, `FromHostAssemblies()`, and the approved `WithAssemblyProvider(...)` overload family while rejecting `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, and `AddHostAssemblies`.

### Tests for User Story 2 ⚠️

> **NOTE: Add the focused guardrails before finalizing implementation so they would fail if the approved names drift.**

- [X] T007 [P] [US2] Add public-surface naming guardrails in `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderNamingGuardrailTests.cs` that require `FromAssemblies(...)`, `FromHostAssemblies()`, and the `WithAssemblyProvider(...)` overload family while rejecting `WithAssemblies`, `WithHostAssemblies`, `AddAssemblies`, and `AddHostAssemblies`
- [X] T008 [P] [US2] Update `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs` to keep behavior-level coverage aligned with the approved `From*`/`With*` surface and supported `WithAssemblyProvider(...)` overload shapes

**Checkpoint**: The approved naming surface is protected by focused automated regression coverage.

---

## Phase 5: User Story 3 - Keep Developer-Facing Guidance Consistent (Priority: P3)

**Goal**: Align only the existing docs, samples, and guidance that already describe assembly discovery so they reinforce the shipped naming surface without broad rewrites.

**Independent Test**: Review the in-scope guidance assets and confirm they consistently teach `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`, reinforce the `From*` versus `With*` distinction, and avoid rejected alternative names.

### Implementation for User Story 3

- [X] T009 [P] [US3] Align assembly-discovery guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/README.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/getting-started.md`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/docs/multiple-shell-providers.md` so they reinforce the approved names without teaching replacement verbs
- [X] T010 [P] [US3] Align assembly-discovery guidance in `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/README.md`, `/Users/sipke/Projects/ValenceWorks/cshells/main/wiki/Getting-Started.md`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/Program.cs` so examples and comments use the approved names and valid current overloads only

**Checkpoint**: All in-scope guidance assets describe assembly discovery with the same approved vocabulary as the code.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate the completed implementation with focused tests and targeted builds.

- [X] T011 Run `dotnet test /Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/ --filter "FullyQualifiedName~CShellsBuilder"` to validate `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderNamingGuardrailTests.cs` and `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs`
- [X] T012 Run `dotnet test /Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/ --filter "FullyQualifiedName~FeatureAssemblySelectionIntegrationTests"` to validate `/Users/sipke/Projects/ValenceWorks/cshells/main/tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs`
- [X] T013 Run targeted build validation for `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells/CShells.csproj`, `/Users/sipke/Projects/ValenceWorks/cshells/main/src/CShells.AspNetCore/CShells.AspNetCore.csproj`, and `/Users/sipke/Projects/ValenceWorks/cshells/main/samples/CShells.Workbench/CShells.Workbench.csproj`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1: Setup**: No dependencies; start immediately.
- **Phase 2: Foundational**: Depends on Phase 1 and blocks all user stories.
- **Phase 3: User Story 1 (P1)**: Depends on Phase 2 because the final guardrails and guidance must target the locked public surface.
- **Phase 4: User Story 2 (P2)**: Depends on User Story 1 so the regression tests lock the final approved code surface rather than an intermediate state.
- **Phase 5: User Story 3 (P3)**: Depends on User Story 1; it can proceed after the code surface is stable, even though normal delivery still follows P1 → P2 → P3 priority order.
- **Phase 6: Polish**: Depends on all desired user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: MVP; no dependency on later stories.
- **User Story 2 (P2)**: Depends on User Story 1 because the new guardrails must reflect the preserved public naming surface.
- **User Story 3 (P3)**: Depends on User Story 1 because docs, samples, and comments must mirror the final approved code surface.

### Within Each User Story

- Preserve the public API before locking it with tests.
- Add focused tests before widening any documentation/sample cleanup.
- Keep guidance edits restricted to assets that already describe assembly discovery.
- Finish with targeted validation only; avoid unrelated renames or broad repository sweeps.

### Parallel Opportunities

- T001 and T002 can run in parallel during scope lock.
- T005 and T006 can run in parallel after T004 stabilizes the approved code surface.
- T007 and T008 can run in parallel inside the existing xUnit project because they touch different test files.
- T009 and T010 can run in parallel because they touch different guidance assets.

---

## Parallel Example: User Story 1

```bash
# After T004 preserves the public code surface, align the code-adjacent remarks in parallel:
Task: "Align the assembly-discovery remarks in src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs"
Task: "Align the assembly-discovery remarks in src/CShells.AspNetCore/Extensions/ShellExtensions.cs"
```

## Parallel Example: User Story 2

```bash
# Build the focused regression coverage in parallel inside the existing xUnit project:
Task: "Add public-surface naming guardrails in tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderNamingGuardrailTests.cs"
Task: "Update tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs"
```

## Parallel Example: User Story 3

```bash
# Split the guidance cleanup by asset group:
Task: "Align assembly-discovery guidance in README.md, docs/getting-started.md, and docs/multiple-shell-providers.md"
Task: "Align assembly-discovery guidance in src/CShells.AspNetCore/README.md, wiki/Getting-Started.md, and samples/CShells.Workbench/Program.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate the approved assembly-discovery surface in code before adding guardrails or guidance cleanup.

### Incremental Delivery

1. Lock the minimal implementation/test/doc scope.
2. Preserve the approved public naming surface in code and code-adjacent comments.
3. Add focused xUnit guardrails that keep the approved names fixed.
4. Align only the already relevant docs, samples, and guidance assets.
5. Finish with targeted tests and builds.

### Parallel Team Strategy

1. One contributor can baseline code/tests while another baselines docs/samples during Phase 1.
2. After User Story 1 stabilizes the code surface, split the ASP.NET Core comment alignment work across two contributors.
3. During User Story 2, one contributor can add the new naming guardrail file while another tightens the existing assembly-source behavior tests.
4. During User Story 3, split the markdown asset cleanup from the sample/package guidance cleanup.

---

## Notes

- `[P]` tasks touch different files and can be completed in parallel safely.
- `[US1]`, `[US2]`, and `[US3]` map directly to the prioritized stories in `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/004-builder-verb-naming/spec.md`.
- This task list keeps the approved names fixed as `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`.
- Scope is intentionally minimal: preserve the current naming surface, add focused regression guardrails, align only in-scope guidance, and validate with targeted tests/builds.

