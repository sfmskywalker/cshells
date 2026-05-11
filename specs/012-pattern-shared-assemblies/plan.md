# Implementation Plan: Pattern-Based Shared Assemblies

**Branch**: `012-pattern-shared-assemblies` | **Date**: 2026-05-11 | **Spec**: [spec.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/spec.md)
**Input**: Feature specification from `/specs/012-pattern-shared-assemblies/spec.md`

## Summary

Add host-wide shared assembly selection for feature discovery using one root `CShells:SharedAssemblies` collection and matching code-first builder APIs. Entries without `*` match exact assembly simple names, entries ending in `*` match simple-name prefixes, and predicate selectors are code-first only. The implementation will introduce public selector abstractions in `CShells.Abstractions`, resolver/matching implementation in `CShells`, validation and diagnostics for invalid selectors, and documentation showing safe framework-family usage.

## Technical Context

**Language/Version**: C# 14 / .NET 10; source projects multi-target `net8.0;net9.0;net10.0` per repository conventions  
**Primary Dependencies**: Existing `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`, `System.Reflection`, `Microsoft.Extensions.DependencyModel`; no new third-party packages  
**Storage**: N/A; selectors come from configuration and code-first registrations only  
**Testing**: xUnit in `tests/CShells.Tests/` with unit and configuration-focused tests  
**Target Platform**: .NET library used by ASP.NET Core and generic host applications  
**Project Type**: Multi-package .NET library  
**Performance Goals**: Shared assembly filtering remains linear over candidate assembly names; selector matching should avoid repeated parsing per assembly by compiling/validating selectors once during builder/configuration setup  
**Constraints**: Match only assembly simple names, case-insensitively; `*` may appear only as final character; selectors are host-wide and not per-shell; no new third-party packages; public extensibility contracts belong in abstractions first  
**Scale/Scope**: Host dependency contexts with tens to hundreds of assembly names and a small shared selector list; exact and pattern deduplication must be deterministic and diagnostics must identify selector sources

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Abstraction-First Architecture**: PASS. Public selector contracts and options will be introduced in `CShells.Abstractions`; resolver implementation remains in `CShells`.
- **II. Feature Modularity**: PASS. The feature affects host-level feature assembly discovery and does not add a shell feature or cross-feature coupling.
- **III. Modern C# Style**: PASS. Plan targets C# 14 conventions, nullable-safe APIs, explicit access modifiers, primary constructors where useful, and collection expressions.
- **IV. Explicit Error Handling**: PASS. Invalid configuration entries and throwing predicates surface actionable exceptions with selector source/path context.
- **V. Test Coverage**: PASS. Unit tests cover grammar, exact/prefix matching, source deduplication, configuration binding, builder APIs, and resolver integration.
- **VI. Simplicity & Minimalism**: PASS. Use one unified `SharedAssemblies` collection and no new packages; add only the abstractions required for public predicate/source diagnostics.
- **VII. Lifecycle & Concurrency Contracts**: PASS. No shell lifecycle state changes; selector registries are constructed during host setup and read-only during discovery.

## Project Structure

### Documentation (this feature)

```text
specs/012-pattern-shared-assemblies/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── shared-assemblies.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   └── Features/
│       ├── ISharedAssemblySelector.cs
│       └── SharedAssemblyMatch.cs
├── CShells/
│   ├── Configuration/
│   │   └── CShellsOptions.cs
│   ├── DependencyInjection/
│   │   ├── CShellsBuilder.cs
│   │   └── CShellsBuilderExtensions.cs
│   └── Features/
│       ├── FeatureAssemblyResolver.cs
│       ├── SharedAssemblySelector.cs
│       ├── SharedAssemblySelectorProvider.cs
│       └── SharedAssemblyPattern.cs
└── CShells.AspNetCore/
    └── Extensions/
        └── ShellExtensions.cs

tests/
└── CShells.Tests/
    └── Unit/
        ├── Configuration/
        │   └── SharedAssemblyConfigurationTests.cs
        ├── DependencyInjection/
        │   └── CShellsBuilderSharedAssemblyTests.cs
        └── Features/
            ├── SharedAssemblyPatternTests.cs
            └── FeatureAssemblyResolverSharedAssemblyTests.cs

docs/
├── getting-started.md
└── integration-patterns.md

README.md
samples/
└── CShells.Workbench/
    └── appsettings.json
```

**Structure Decision**: Implement as a host-level library capability in the existing CShells packages. Public selector/match contracts live in `CShells.Abstractions`, while parsing, matching, configuration binding, and feature assembly filtering live in `CShells`. Tests mirror the touched source areas under `tests/CShells.Tests/Unit/`.

## Phase 0: Research

Research completed in [research.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/research.md). No `NEEDS CLARIFICATION` items remain.

## Phase 1: Design & Contracts

Design artifacts:

- [data-model.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/data-model.md)
- [contracts/shared-assemblies.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/contracts/shared-assemblies.md)
- [quickstart.md](/Users/sipke/Projects/ValenceWorks/cshells/main/specs/012-pattern-shared-assemblies/quickstart.md)

## Constitution Check

*Post-design re-check.*

- **I. Abstraction-First Architecture**: PASS. Contracts file explicitly places public selector interfaces/records in `CShells.Abstractions`.
- **II. Feature Modularity**: PASS. The design composes with existing feature discovery and does not alter feature dependency resolution.
- **III. Modern C# Style**: PASS. Contracts and implementation are planned as nullable-safe C# APIs with public XML docs.
- **IV. Explicit Error Handling**: PASS. Invalid entries, unsupported wildcard positions, null predicates, null assemblies, and throwing predicates have defined error behavior.
- **V. Test Coverage**: PASS. Quickstart and contracts define verifiable unit/integration scenarios for every success criterion.
- **VI. Simplicity & Minimalism**: PASS. One unified collection avoids parallel schema and minimizes API surface.
- **VII. Lifecycle & Concurrency Contracts**: PASS. Host-level selector state is immutable after builder/configuration composition and does not introduce async mutable lifecycle state.

## Complexity Tracking

No constitution violations requiring justification.
