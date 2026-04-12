# Implementation Plan: Fluent Builder Naming Matrix

**Branch**: `004-builder-verb-naming` | **Date**: 2026-04-12 | **Spec**: `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/004-builder-verb-naming/spec.md`
**Input**: Feature specification from `/specs/004-builder-verb-naming/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Keep feature `004-builder-verb-naming` as the decision record for the fluent builder naming matrix, but update its execution scope so it also protects the already shipped assembly-discovery API in the repository. The implementation should preserve `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)` as the fixed public naming surface in `CShellsBuilderExtensions`, add minimal regression guardrails that fail if those names drift or if rejected replacement verbs are introduced, and audit the small set of in-scope docs, samples, and comments that explain assembly discovery so they continue to reinforce the same `From*` versus `With*` distinction.

## Technical Context

**Language/Version**: C# 14 / .NET 10 for implementation and xUnit tests, plus Markdown planning and documentation artifacts  
**Primary Dependencies**: Existing public builder surface in `src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`, current assembly-source coverage in `tests/CShells.Tests/Unit/DependencyInjection/CShellsBuilderAssemblySourceTests.cs` and `tests/CShells.Tests/Integration/ShellHost/FeatureAssemblySelectionIntegrationTests.cs`, prior-art feature `specs/003-fluent-assembly-selection/`, and developer guidance in `README.md`, `docs/`, `wiki/`, `samples/`, and `src/CShells.AspNetCore/`  
**Storage**: N/A  
**Testing**: xUnit regression guardrails for public method names and approved overload families, plus targeted review of in-scope guidance assets and reuse of existing assembly-discovery behavior tests as supporting evidence  
**Target Platform**: Cross-platform .NET class libraries and ASP.NET Core integration guidance consumed from the main package, ASP.NET Core package, docs, wiki, and sample app  
**Project Type**: Multi-project framework/library with tests, samples, and public markdown documentation  
**Performance Goals**: No runtime behavior change; added verification should stay limited to startup-time test reflection and documentation review  
**Constraints**: Preserve the approved naming matrix (`From*` for source selection, `With*` for provider attachment); keep the approved public names fixed as `FromAssemblies(...)`, `FromHostAssemblies()`, and `WithAssemblyProvider(...)`; allow multiple valid `WithAssemblyProvider(...)` overloads without treating them as drift; avoid unrelated renames or new aliases; add no new third-party dependencies; keep scope minimal and grounded in existing repository reality  
**Scale/Scope**: Planning artifacts in `specs/004-builder-verb-naming/`, likely implementation touch points in `src/CShells/DependencyInjection/CShellsBuilderExtensions.cs`, a focused unit test in `tests/CShells.Tests/Unit/DependencyInjection/`, and only the guidance assets that already describe assembly discovery (`README.md`, `docs/getting-started.md`, `docs/multiple-shell-providers.md`, `src/CShells.AspNetCore/README.md`, `src/CShells.AspNetCore/Extensions/ServiceCollectionExtensions.cs`, `src/CShells.AspNetCore/Extensions/ShellExtensions.cs`, `wiki/Getting-Started.md`, and `samples/CShells.Workbench/Program.cs` if naming guidance there needs cleanup)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Abstraction-First Architecture**: PASS. The approved public API already exists and no new public extensibility contract is required; the work is limited to preserving names, testing them, and aligning guidance.
- **Feature Modularity**: PASS. The change scope stays inside assembly-discovery builder vocabulary and does not alter feature composition, shell isolation, or dependency semantics.
- **Modern C# Style**: PASS. Any implementation work is expected to be limited to existing extension methods and xUnit tests inside established projects and conventions.
- **Explicit Error Handling**: PASS. Regression guardrails strengthen early failure for naming drift without changing runtime exception paths.
- **Test Coverage**: PASS. The feature now explicitly requires automated guardrails, so the plan includes focused xUnit coverage for the approved naming surface and preserves existing behavior tests as regression support.
- **Simplicity & Minimalism**: PASS. A reflection-based or equivalent public-surface test in the existing test project is the smallest repository-native guardrail that protects the naming decision without adding analyzers, packages, or broad refactors.

**Post-Design Re-check**: PASS. The design keeps the feature implementation-backed but narrow: preserve the current API surface, add targeted verification, align only already relevant guidance assets, and avoid introducing new abstractions or unrelated public renames.

## Project Structure

### Documentation (this feature)

```text
specs/004-builder-verb-naming/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── builder-naming-matrix.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── CShells/
│   └── DependencyInjection/
│       └── CShellsBuilderExtensions.cs
├── CShells.AspNetCore/
│   ├── README.md
│   └── Extensions/
│       ├── ServiceCollectionExtensions.cs
│       └── ShellExtensions.cs
├── README.md
├── docs/
│   ├── getting-started.md
│   └── multiple-shell-providers.md
├── wiki/
│   └── Getting-Started.md
└── samples/
    └── CShells.Workbench/
        └── Program.cs

tests/
└── CShells.Tests/
    ├── Unit/
    │   └── DependencyInjection/
    │       ├── CShellsBuilderAssemblySourceTests.cs
    │       └── CShellsBuilderNamingGuardrailTests.cs        # likely new focused guardrail test
    └── Integration/
        └── ShellHost/
            └── FeatureAssemblySelectionIntegrationTests.cs
```

**Structure Decision**: Keep feature 004 centered on the existing assembly-discovery builder surface in `CShellsBuilderExtensions`. Protect that public surface with focused xUnit guardrails in the existing `tests/CShells.Tests` project, treat the current integration tests as behavior-level regression support, and limit documentation/sample work to the assets that already describe assembly discovery. This delivers a real execution scope for downstream tasks without reopening the approved naming decision or expanding into unrelated builder APIs.

## Complexity Tracking

No constitution violations or justified complexity exceptions were identified during planning.

