# Implementation Plan: Polymorphic Feature Configuration

**Branch**: `014-polymorphic-feature-config` | **Date**: 2026-05-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/014-polymorphic-feature-config/spec.md`

## Summary

Keep per-shell `Features` as a named map while allowing each feature value to be `true`, `false`, or an object. The implementation will extend feature entry parsing to preserve explicit enable/disable declarations, merge code-first defaults with configuration declarations by precedence, reset feature settings on higher-priority `true`, and continue binding object feature settings directly under the feature name.

## Technical Context

**Language/Version**: C# 14 / .NET 10; source projects multi-target `net8.0;net9.0;net10.0` per repository conventions  
**Primary Dependencies**: Existing `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`; no new third-party packages  
**Storage**: N/A; configuration provider inputs only  
**Testing**: xUnit in `tests/CShells.Tests`; focused unit tests for parsing/merging plus integration-level activation behavior where needed  
**Target Platform**: .NET library/runtime used by ASP.NET Core hosts and background workers  
**Project Type**: Multi-package .NET library  
**Performance Goals**: Feature parsing and merging remain linear in configured feature declarations per shell; no additional shell activation passes  
**Constraints**: Preserve direct feature option binding paths; do not introduce `Settings`, `EnabledFeatures`, or `DisabledFeatures` configuration sections; code-first registrations are overridable defaults  
**Scale/Scope**: Per-shell feature maps and layered configuration sources; no persistent storage or external services

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Abstraction-First Architecture**: PASS. Runtime feature declarations affect public `ShellSettings` semantics, so any required model state belongs in `CShells.Abstractions` before implementation uses it.
- **II. Feature Modularity**: PASS. The feature remains declarative shell configuration; no feature constructor/service coupling changes.
- **III. Modern C# Style**: PASS. Planned edits use existing C# 14 conventions, collection expressions, explicit access modifiers, nullable annotations, and XML docs for public members.
- **IV. Explicit Error Handling**: PASS. Invalid values, nulls, blank feature names, and unknown positive feature entries will fail with actionable messages that name the feature path.
- **V. Test Coverage**: PASS. Unit and integration tests are planned for all new value forms, merge precedence, option reset behavior, environment-style strings, and unknown-feature behavior.
- **VI. Simplicity & Minimalism**: PASS. The design extends the existing feature entry model and parsing helpers rather than adding a separate configuration subsystem.
- **VII. Lifecycle & Concurrency Contracts**: PASS. The feature affects composition before activation and does not add shared mutable lifecycle state or async coordination.

## Project Structure

### Documentation (this feature)

```text
specs/014-polymorphic-feature-config/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── feature-configuration.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   └── ShellSettings.cs
└── CShells/
    ├── Configuration/
    │   ├── FeatureEntry.cs
    │   ├── FeatureEntryListJsonConverter.cs
    │   ├── ConfigurationHelper.cs
    │   ├── ShellBuilder.cs
    │   └── ShellConfig.cs
    └── Lifecycle/
        ├── Blueprints/ConfigurationShellBlueprint.cs
        ├── Providers/ConfiguredShellBlueprintProvider.cs
        └── ShellProviderBuilder.cs

tests/
└── CShells.Tests/
    ├── Unit/
    │   ├── Configuration/
    │   ├── Lifecycle/Blueprints/
    │   └── ShellBuilderTests.cs
    └── Integration/
        └── FeatureDependency/
```

**Structure Decision**: Implement in the existing core configuration and lifecycle composition paths. `ShellSettings` carries final enabled features plus explicit disabled declarations needed for precedence merging; configuration helpers normalize all supported input forms into that model.

## Complexity Tracking

No constitution violations or added complexity exceptions.

## Phase 0: Research

Research decisions are captured in [research.md](./research.md).

## Phase 1: Design & Contracts

Design artifacts:

- [data-model.md](./data-model.md)
- [contracts/feature-configuration.md](./contracts/feature-configuration.md)
- [quickstart.md](./quickstart.md)

### Post-Design Constitution Check

- **I. Abstraction-First Architecture**: PASS. Public composition state is represented in `CShells.Abstractions/ShellSettings.cs`; parsing and activation behavior remains in implementation projects.
- **II. Feature Modularity**: PASS. The resolved feature set still feeds existing dependency resolution and feature activation.
- **III. Modern C# Style**: PASS. Planned public model changes require XML documentation and existing naming/style conventions.
- **IV. Explicit Error Handling**: PASS. The contract defines precise accepted values and actionable failures.
- **V. Test Coverage**: PASS. Design maps every clarified behavior to focused tests.
- **VI. Simplicity & Minimalism**: PASS. No new packages, storage, or alternate configuration section hierarchy.
- **VII. Lifecycle & Concurrency Contracts**: PASS. No lifecycle state machine or concurrent mutation changes.
