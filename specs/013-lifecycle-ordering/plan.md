# Implementation Plan: Lifecycle Ordering

**Branch**: `013-lifecycle-ordering` | **Date**: 2026-05-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/013-lifecycle-ordering/spec.md`

## Summary

Add first-class lifecycle ordering for shell initializers so feature authors can keep `ShellFeatureAttribute.DependsOn` as a dependencies-first service-configuration contract while independently declaring activation order for `IShellInitializer` work. The implementation will introduce public lifecycle phase/order metadata and transient `AddShellInitializer<TInitializer>()` authoring APIs in `CShells.Abstractions`, add an initializer ordering planner in `CShells`, assign unordered initializers to the `Default` phase between `Prepare` and `Start`, preserve existing drain parallelism, and document provider/base feature usage with the Quartz-style migration-before-scheduler case.

## Technical Context

**Language/Version**: C# 14 / .NET 10; source projects multi-target `net8.0;net9.0;net10.0` per repository conventions  
**Primary Dependencies**: Existing `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `System.Reflection`, and CShells lifecycle/feature abstractions; no new third-party packages  
**Storage**: N/A; lifecycle ordering is contributed by feature service registrations and type metadata only  
**Testing**: xUnit in `tests/CShells.Tests/` with lifecycle-focused integration tests and ordering-planner unit tests  
**Target Platform**: .NET library used by ASP.NET Core and generic host applications  
**Project Type**: Multi-package .NET library  
**Performance Goals**: Initializer planning remains linear or near-linear over registered lifecycle entries; no measurable overhead for shells with zero initializers beyond current activation behavior  
**Constraints**: Preserve feature dependency semantics; use semantic phases plus numeric order only; do not add before/after relationship graphs; unordered initializers run in `Default` between `Prepare` and `Start`; `AddShellInitializer<T>()` registers transient initializers; ordered initializers resolve from the shell provider at execution time; keep drain handlers parallel; no new third-party packages; public extensibility contracts belong in abstractions first  
**Scale/Scope**: Per-shell initializer lists are expected to be small, usually 0-20 entries; diagnostics must remain actionable when multiple features contribute lifecycle registrations

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Abstraction-First Architecture**: PASS. Public lifecycle phase, attribute, registration metadata, and `IServiceCollection` extensions live in `CShells.Abstractions`; ordering planner and registry execution changes remain in `CShells`.
- **II. Feature Modularity**: PASS. Feature dependency ordering remains unchanged, and feature authors express lifecycle work through feature-owned initializer registrations.
- **III. Modern C# Style**: PASS. Plan targets nullable-safe C# 14 APIs, XML docs for public members, explicit access modifiers, and collection expressions.
- **IV. Explicit Error Handling**: PASS. Invalid metadata, missing/mismatched lifecycle entries, and duplicate ambiguity receive actionable diagnostics naming the shell and initializer types.
- **V. Test Coverage**: PASS. Tests cover dependency order versus initializer order, default compatibility, explicit ordering, invalid metadata diagnostics, drain compatibility, transient lifetime, and the Quartz-style provider/base scenario.
- **VI. Simplicity & Minimalism**: PASS. Initial release uses phase plus numeric ordering only, avoids before/after graphs, and defers ordered drain execution.
- **VII. Lifecycle & Concurrency Contracts**: PASS. Initializers remain sequential during activation, activation failure still aborts promotion to Active, and drain concurrency remains parallel/idempotent.

## Project Structure

### Documentation (this feature)

```text
specs/013-lifecycle-ordering/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── lifecycle-ordering.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   ├── Lifecycle/
│   │   ├── LifecyclePhase.cs
│   │   ├── LifecycleOrderAttribute.cs
│   │   ├── ShellInitializerRegistration.cs
│   │   ├── ShellInitializerOrderException.cs
│   │   ├── ServiceCollectionLifecycleExtensions.cs
│   │   ├── IDrainHandler.cs
│   │   └── IShellInitializer.cs
│   └── README.md
├── CShells/
│   ├── Lifecycle/
│   │   ├── ShellInitializerOrderingPlanner.cs
│   │   └── ShellRegistry.cs
│   └── README.md

tests/
└── CShells.Tests/
    ├── Unit/
    │   └── Lifecycle/
    │       └── ShellInitializerOrderingPlannerTests.cs
    └── Integration/
        └── Lifecycle/
            ├── ShellRegistryInitializerTests.cs
            └── ShellRegistryDrainTests.cs

docs/
└── integration-patterns.md
```

**Structure Decision**: Implement as a lifecycle capability inside the existing abstraction and implementation packages. Public authoring APIs live in `CShells.Abstractions/Lifecycle` because feature packages should only reference abstractions. Runtime planning, validation, and diagnostics live in `CShells/Lifecycle`, with focused tests mirroring those areas.

## Phase 0: Research

Research completed in [research.md](./research.md). No `NEEDS CLARIFICATION` items remain.

## Phase 1: Design & Contracts

Design artifacts:

- [data-model.md](./data-model.md)
- [contracts/lifecycle-ordering.md](./contracts/lifecycle-ordering.md)
- [quickstart.md](./quickstart.md)

## Constitution Check

*Post-design re-check.*

- **I. Abstraction-First Architecture**: PASS. The contract file places public lifecycle registration APIs and ordering metadata in `CShells.Abstractions`.
- **II. Feature Modularity**: PASS. Provider/base examples keep feature dependency declarations for configuration and use lifecycle order only for activation work.
- **III. Modern C# Style**: PASS. The planned public APIs are documented, nullable-aware, and consistent with repository style.
- **IV. Explicit Error Handling**: PASS. The design defines activation-failing diagnostics for invalid metadata and deterministic tie handling for equal phase/order entries.
- **V. Test Coverage**: PASS. Contract and quickstart scenarios map directly to unit and integration tests.
- **VI. Simplicity & Minimalism**: PASS. The plan avoids a general workflow engine, avoids relationship graphs, and does not add ordered drain execution.
- **VII. Lifecycle & Concurrency Contracts**: PASS. Initializer execution remains sequential inside activation; drain handler parallelism and cancellation behavior remain unchanged.

## Complexity Tracking

No constitution violations requiring justification.
