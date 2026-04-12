# Implementation Plan: Fluent Assembly Source Selection

**Branch**: `003-fluent-assembly-selection` | **Date**: 2026-04-12 | **Spec**: `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/003-fluent-assembly-selection/spec.md`
**Input**: Feature specification from `/specs/003-fluent-assembly-selection/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Refactor CShells feature discovery so assembly selection moves from trailing `AddCShells`/`AddShells` method arguments into fluent `CShellsBuilder` configuration backed by a builder-managed assembly-provider list. The implementation will introduce a public feature-assembly provider abstraction in `CShells.Abstractions`, add built-in host-derived and explicit-assembly providers in `CShells`, preserve current host-derived discovery only when no assembly-source calls are made, support additive composition across built-in and custom providers, remove the legacy non-fluent assembly-argument path, and capture the approved naming set for the new public API surface.

## Technical Context

**Language/Version**: C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`)  
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.DependencyModel`, `Microsoft.AspNetCore.Builder`, existing `CShells.Features` discovery code, `CShells.DependencyInjection` builder extensions, and ASP.NET Core registration helpers  
**Storage**: N/A  
**Testing**: xUnit with `Assert.*`, unit tests in `tests/CShells.Tests/Unit/`, integration tests in `tests/CShells.Tests/Integration/`  
**Target Platform**: Cross-platform .NET class libraries consumed by ASP.NET Core applications and feature class libraries  
**Project Type**: Multi-project framework/library with ASP.NET Core integration, samples, and markdown documentation  
**Performance Goals**: Preserve startup-only feature discovery costs that remain linear in configured provider count plus deduplicated assembly count, while ensuring each expected feature is discovered exactly once  
**Constraints**: New public extensibility contracts must live in `CShells.Abstractions`; breaking changes are intentional and must remove legacy assembly-argument overloads; no new third-party dependencies; explicit assembly-source mode must suppress implicit host-derived scanning unless `FromHostAssemblies()` is appended; docs/examples must reflect only the new fluent model  
**Scale/Scope**: Changes span `src/CShells.Abstractions`, `src/CShells`, `src/CShells.AspNetCore`, focused unit/integration tests, and developer-facing guidance in `README.md`, `docs/`, and possibly `wiki/`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Abstraction-First Architecture**: PASS. The new public custom-provider extension point is planned as `IFeatureAssemblyProvider` in `src/CShells.Abstractions`, while built-in providers and registration behavior remain in `src/CShells` and `src/CShells.AspNetCore`.
- **Feature Modularity**: PASS. The feature changes discovery-source selection only; discovered feature semantics, dependency ordering, and per-shell isolation remain intact.
- **Modern C# Style**: PASS. The work fits the existing file-scoped namespaces, nullable reference types, explicit access modifiers, collection expressions, and XML-doc-heavy public API conventions.
- **Explicit Error Handling**: PASS. Null assembly inputs, null provider registrations, and other invalid discovery-source inputs will be planned as fail-fast validation paths with actionable guidance.
- **Test Coverage**: PASS. The plan includes targeted unit tests for builder/provider behavior, integration tests for default-vs-explicit host discovery semantics, and coverage for custom provider composition.
- **Simplicity & Minimalism**: PASS. The design reuses the existing fluent builder pattern and current host-assembly resolution logic instead of introducing a parallel discovery subsystem.

**Post-Design Re-check**: PASS. Research and design keep the change centered on one new public abstraction, a builder-managed provider list, reusable host-assembly resolution, focused tests, and documentation updates without speculative extra layers.

## Project Structure

### Documentation (this feature)

```text
specs/003-fluent-assembly-selection/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── feature-assembly-provider-contract.md
│   └── naming-decision-record.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   └── Features/
│       └── IFeatureAssemblyProvider.cs                    # new public discovery-source contract
├── CShells/
│   ├── DependencyInjection/
│   │   ├── CShellsBuilder.cs                              # builder-managed assembly-provider registrations
│   │   ├── CShellsBuilderExtensions.cs                    # FromAssemblies / FromHostAssemblies / WithAssemblyProvider APIs + XML docs
│   │   └── ServiceCollectionExtensions.cs                 # default-vs-explicit provider selection and host wiring
│   └── Features/
│       ├── ExplicitFeatureAssemblyProvider.cs             # built-in explicit-assembly provider
│       ├── HostFeatureAssemblyProvider.cs                 # built-in host-derived provider
│       └── FeatureAssemblyResolver.cs                     # extracted host-resolution and aggregation helper
├── CShells.AspNetCore/
│   └── Extensions/
│       ├── ServiceCollectionExtensions.cs                 # fluent-only AddCShellsAspNetCore surface
│       └── ShellExtensions.cs                             # fluent-only AddShells overload surface
└── CShells.AspNetCore/README.md                           # ASP.NET Core setup guidance if examples change

tests/
└── CShells.Tests/
    ├── Integration/
    │   └── ShellHost/
    │       └── FeatureAssemblySelectionIntegrationTests.cs
    └── Unit/
        ├── DependencyInjection/
        │   └── CShellsBuilderAssemblySourceTests.cs
        └── Features/
            ├── FeatureAssemblyProviderTests.cs
            └── HostFeatureAssemblyProviderTests.cs

README.md
docs/
├── getting-started.md
├── integration-patterns.md
└── multiple-shell-providers.md

wiki/
└── Getting-Started.md                                     # if public examples are mirrored here
```

**Structure Decision**: Keep the public extensibility contract in `CShells.Abstractions/Features` because assembly selection is part of feature discovery and must remain available to third-party feature libraries without implementation-project references. Implement built-in providers and aggregation logic in `src/CShells`, thread the builder-managed provider list through the existing registration pipeline, remove legacy assembly-argument overloads from both core and ASP.NET Core entry points, and verify behavior through focused builder/discovery tests plus documentation updates at the main entry points users already follow.

## Complexity Tracking

No constitution violations or justified complexity exceptions were identified during planning.
