# Implementation Plan: Feature Object Map

**Branch**: `002-feature-object-map` | **Date**: 2026-03-14 | **Spec**: `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/002-feature-object-map/spec.md`
**Input**: Feature specification from `/specs/002-feature-object-map/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add object-map support for shell `Features` configuration while preserving existing array behavior. The implementation will extend configuration parsing and `System.Text.Json` collection conversion at the `ShellConfig.Features` boundary, keep the map key as the only feature identifier in object-map syntax, preserve declaration order before dependency resolution, reject ambiguous mixed-shape inputs, reject duplicate configured feature names across supported input forms, and prefer object-map output when serializing shell configuration models.

## Technical Context

**Language/Version**: C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`)  
**Primary Dependencies**: `Microsoft.Extensions.Configuration`, `System.Text.Json`, existing `CShells.Configuration` helpers and converters, FluentStorage JSON provider integration  
**Storage**: N/A at the feature level; shell definitions originate from configuration providers, in-memory/config models, and FluentStorage JSON blobs  
**Testing**: xUnit with `Assert.*`, unit tests in `tests/CShells.Tests/Unit/`, integration tests in `tests/CShells.Tests/Integration/`  
**Target Platform**: Cross-platform .NET class library consumed by ASP.NET Core applications and provider integrations
**Project Type**: Multi-project framework/library with samples and markdown documentation  
**Performance Goals**: Preserve current feature-binding behavior with linear parsing/serialization cost relative to configured feature count; avoid additional shell-build or dependency-resolution work outside existing flows  
**Constraints**: Maintain backward compatibility for array syntax, preserve feature configuration access paths, keep JSON/config-provider behavior consistent, reject ambiguous mixed-shape inputs explicitly, reject duplicate configured feature names across supported input forms, and preserve declaration order before dependency ordering  
**Scale/Scope**: Changes span `src/CShells/Configuration`, `src/CShells.Providers.FluentStorage`, focused unit/integration tests, and public-facing configuration documentation/samples where syntax examples are shown

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Abstraction-First Architecture**: PASS. The feature changes configuration parsing and serialization behavior inside existing public models and converters without introducing new public implementation contracts that need to move into an abstractions assembly.
- **Feature Modularity**: PASS. The change affects shell configuration representation only; it does not add cross-feature coupling or alter feature dependency declarations.
- **Modern C# Style**: PASS. Planned work fits the repository’s file-scoped namespaces, nullable annotations, explicit access modifiers, and collection-expression usage.
- **Explicit Error Handling**: PASS. Ambiguous or invalid object-map definitions will fail clearly instead of being silently coerced.
- **Test Coverage**: PASS. The feature requires unit coverage for JSON conversion and settings factory behavior plus integration coverage for `IConfiguration` binding behavior.
- **Simplicity & Minimalism**: PASS. The design extends the existing feature collection boundary rather than introducing a second configuration model or speculative abstraction.

**Post-Design Re-check**: PASS. The research and design keep the change contained to existing configuration seams, use focused validation instead of new abstractions, and require targeted tests and documentation updates only.

## Project Structure

### Documentation (this feature)

```text
specs/002-feature-object-map/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── feature-configuration-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── CShells/
│   └── Configuration/
│       ├── ConfigurationHelper.cs
│       ├── FeatureEntry.cs
│       ├── FeatureEntryJsonConverter.cs
│       ├── FeatureEntryListJsonConverter.cs
│       ├── ShellBuilder.cs
│       ├── ShellConfig.cs
│       └── ShellSettingsFactory.cs
└── CShells.Providers.FluentStorage/
    └── FluentStorageShellSettingsProvider.cs

tests/
└── CShells.Tests/
    ├── Integration/
    │   └── Configuration/
    │       └── ConfigurationBindingTests.cs
    └── Unit/
        ├── Configuration/
        │   ├── FeatureEntryJsonConverterTests.cs
        │   └── ShellConfigurationTests.cs
        └── ShellSettingsFactoryTests.cs

samples/
└── CShells.Workbench/
    └── appsettings.json

docs/
└── feature-configuration.md

wiki/
└── Feature-Configuration.md
```

**Structure Decision**: Keep the implementation in the existing `CShells.Configuration` boundary because both configuration-provider parsing and direct JSON deserialization already converge there. Extend provider-specific behavior only where `ShellConfig` JSON conversion is wired explicitly (`CShells.Providers.FluentStorage`). Keep tests in the existing configuration-focused unit and integration suites, and update the sample/documentation paths that show supported `Features` syntax.

## Complexity Tracking

No constitution violations or justified complexity exceptions were identified during planning.
