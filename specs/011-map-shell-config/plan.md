# Implementation Plan: Map-Based Shell Configuration

**Branch**: `011-map-shell-config` | **Date**: 2026-05-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/011-map-shell-config/spec.md`

## Summary

Replace shell configuration under `CShells:Shells` with a single supported object-map shape where each child key is the shell name. The runtime configuration provider will derive shell identity exclusively from the child section key, reject numeric child keys as unsupported array syntax, and remove the fallback that treats an inner `Name` value as shell identity. Configuration binding models, documentation, samples, and tests will align with map syntax so environment variable overrides and layered configuration merge by shell name instead of by array index.

The implementation stays inside the existing CShells configuration and lifecycle provider surfaces. Feature configuration remains unchanged: shell entries still contain `Features` using the existing feature map syntax, and feature-specific values continue to flatten into shell configuration data.

## Technical Context

**Language/Version**: C# 14 / .NET 10 — library projects multi-target existing supported target frameworks; tests target the repository test framework configuration  
**Primary Dependencies**: `Microsoft.Extensions.Configuration`, `System.Text.Json`, existing CShells abstractions and lifecycle providers; no new third-party packages  
**Storage**: N/A — configuration is read from existing configuration providers and in-memory test dictionaries  
**Testing**: xUnit 2.x with `Assert.*`; unit tests in `tests/CShells.Tests/Unit/`; integration coverage through existing configuration/lifecycle test helpers where needed  
**Target Platform**: CShells library consumers and ASP.NET Core hosts using the existing configuration provider pipeline  
**Project Type**: Multi-package .NET library with samples and documentation  
**Performance Goals**: Preserve current `ConfigurationShellBlueprintProvider.GetAsync(name)` direct-key lookup behavior for named shells; avoid extra full-section scans for normal named lookups  
**Constraints**: No backward compatibility for array shell syntax; feature map syntax remains unchanged; errors must be clear and occur before shell activation; docs and samples must not show the old shell array shape  
**Scale/Scope**: All configured shells under `CShells:Shells`; layered configuration from application settings, additional config files, and environment variables; sample and documentation updates across the repository

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Abstraction-First Architecture | PASS | Uses existing configuration/lifecycle abstractions; no new public abstraction required. |
| II. Feature Modularity | PASS | Feature declarations remain declarative and map-based; no feature coupling introduced. |
| III. Modern C# Style | PASS | Implementation will follow existing C# 14 conventions, `var`, guard clauses, and collection expressions. |
| IV. Explicit Error Handling | PASS | Unsupported array shell syntax and invalid shell keys will fail with actionable messages naming `CShells:Shells`. |
| V. Test Coverage | PASS | Adds/updates unit tests for map loading, rejection, environment overrides, and layered merge behavior. |
| VI. Simplicity & Minimalism | PASS | Removes compatibility fallback instead of adding dual-format complexity. |
| VII. Lifecycle & Concurrency Contracts | PASS | No lifecycle state-machine or concurrency behavior changes; provider continues to compose blueprints lazily. |

## Project Structure

### Documentation (this feature)

```text
specs/011-map-shell-config/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── configuration-schema.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── CShells/
│   ├── Configuration/
│   │   ├── CShellsOptions.cs
│   │   └── ShellConfig.cs
│   └── Lifecycle/
│       └── Providers/
│           └── ConfigurationShellBlueprintProvider.cs
└── CShells.Abstractions/
    └── Lifecycle/
        └── ShellDescriptor.cs

tests/
└── CShells.Tests/
    └── Unit/
        ├── Configuration/
        └── Lifecycle/
            └── Providers/

samples/
└── CShells.Workbench/
    └── appsettings.json

docs/
wiki/
README.md
src/*/README.md
samples/*/README.md
```

**Structure Decision**: Implement in the existing CShells configuration and lifecycle provider directories because the feature changes configuration shape and shell name resolution only. Tests mirror the touched `src/` paths under `tests/CShells.Tests/Unit/`. Documentation updates cover repository-level docs, package READMEs, wiki pages, and sample configuration files that currently show `CShells:Shells`.

## Complexity Tracking

No constitution violations or added complexity require justification.

## Phase 0: Research

Completed in [research.md](research.md). Decisions:

- Bind/read `CShells:Shells` as named children rather than a list.
- Derive shell identity exclusively from the child section key.
- Reject numeric child keys as unsupported shell array syntax.
- Preserve existing feature map parsing unchanged.
- Document PascalCase shell keys and uppercase environment variable examples.

## Phase 1: Design

Completed design artifacts:

- [data-model.md](data-model.md) defines the shell configuration map, shell definition, shell name, and configuration-layer behavior.
- [contracts/configuration-schema.md](contracts/configuration-schema.md) documents the supported external configuration contract.
- [quickstart.md](quickstart.md) gives verification examples for JSON configuration, environment overrides, and layered merging.

## Phase 1 Constitution Re-Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Abstraction-First Architecture | PASS | No new abstractions; public behavior is documented as configuration contract. |
| II. Feature Modularity | PASS | Existing feature map contract remains intact. |
| III. Modern C# Style | PASS | Planned code changes are small and align with project conventions. |
| IV. Explicit Error Handling | PASS | Design includes early rejection paths for array shell entries and blank names. |
| V. Test Coverage | PASS | Quickstart and data model map directly to unit/integration test tasks. |
| VI. Simplicity & Minimalism | PASS | Removes old shell syntax instead of maintaining a compatibility layer. |
| VII. Lifecycle & Concurrency Contracts | PASS | Blueprint composition remains lazy and stateless with respect to lifecycle transitions. |
