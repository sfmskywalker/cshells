# Implementation Plan: Deferred Shell Activation and Atomic Shell Reconciliation

**Branch**: `005-deferred-shell-activation` | **Date**: 2026-04-15 | **Spec**: `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/005-deferred-shell-activation/spec.md`
**Input**: Feature specification from `/specs/005-deferred-shell-activation/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Redesign shell activation around two separate truths per configured shell: the latest configured desired generation and the last committed applied runtime generation. Shells activate with whatever configured features are available in the catalog, rather than blocking when features are missing. Missing features are recorded on both `ShellContext` and `ShellRuntimeStatus` for operator visibility, and the reconciliation outcome distinguishes `Active` (all features loaded) from `ActiveWithMissingFeatures` (partial feature set). When late-loaded assemblies become available and the user triggers a reload, shells rebuild with the full feature set and transition to `Active`. The `DeferredDueToMissingFeatures` outcome is replaced by `ActiveWithMissingFeatures`. Routing and endpoint registration enumerate applied active runtimes only (both `Active` and `ActiveWithMissingFeatures`), and explicit `Default` behavior remains strict: if `Default` is configured but failed, the runtime reports it as unavailable instead of silently substituting another shell.

## Technical Context

**Language/Version**: C# 14 on .NET 10 for implementation, multi-targeted source projects (`net8.0;net9.0;net10.0`), xUnit for tests, and Markdown planning artifacts  
**Primary Dependencies**: Existing runtime seams in `src/CShells/Hosting/DefaultShellHost.cs`, `src/CShells/Management/DefaultShellManager.cs`, `src/CShells/Configuration/ShellSettingsCache.cs`, `src/CShells/Features/FeatureDiscovery.cs`, `src/CShells.Abstractions/Features/IFeatureAssemblyProvider.cs`, `src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs`, `src/CShells/Resolution/DefaultShellResolverStrategy.cs`, `src/CShells.AspNetCore/Notifications/ShellEndpointRegistrationHandler.cs`, and `src/CShells.AspNetCore/Routing/DynamicShellEndpointDataSource.cs`  
**Storage**: In-memory desired-state and applied-runtime records sourced from `IShellSettingsProvider`; no new external persistence is required for this feature  
**Testing**: xUnit unit tests in `tests/CShells.Tests/Unit/`, integration tests in `tests/CShells.Tests/Integration/`, and ASP.NET Core end-to-end verification in `tests/CShells.Tests.EndToEnd/` for routing and explicit default behavior  
**Target Platform**: Cross-platform .NET class libraries with ASP.NET Core runtime integration  
**Project Type**: Multi-project framework/library with abstractions, implementation packages, tests, docs, wiki, and sample app  
**Performance Goals**: Refresh the runtime feature catalog once per reconciliation operation, keep the current applied runtime serving until a successor candidate is fully ready, avoid tearing down unaffected shells during partial reconciliation, and keep routing/endpoints stable for already applied shells when newer desired generations defer or fail  
**Constraints**: Activate shells with available features (missing features are recorded, not blocking), fail duplicate feature IDs before mutating the applied catalog or applied runtimes, support partial feature loading at startup, keep routing and lifecycle behavior applied-runtime-only, allow breaking changes where necessary, and place any new public operator/extensibility contracts in `*.Abstractions` projects  
**Scale/Scope**: Planning covers `src/CShells.Abstractions/`, `src/CShells/`, `src/CShells.AspNetCore/`, focused documentation updates, and new unit/integration/E2E coverage around reconciliation state, feature catalog refresh, atomic commit semantics, active-only routing, and explicit `Default` resolution

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Abstraction-First Architecture**: PASS. The redesign can keep catalog refresh and candidate-commit orchestration as internal framework seams in `CShells`, while any new operator-visible read model or runtime-state inspection API is planned for `CShells.Abstractions` before implementation.
- **Feature Modularity**: PASS. The work changes shell orchestration, catalog refresh, routing eligibility, and lifecycle semantics without weakening per-feature isolation, dependency ordering, or constructor constraints.
- **Modern C# Style**: PASS. The plan stays within the repository’s existing C# 14, file-scoped namespace, nullable, explicit-access, and xUnit conventions.
- **Explicit Error Handling**: PASS. Deferred missing-feature outcomes, failed candidate builds, duplicate feature ID refresh failures, and explicit `Default` unavailability will all be surfaced as first-class outcomes instead of silent mutation or fallback.
- **Test Coverage**: PASS. The feature requires new unit, integration, and likely E2E coverage for mixed shell states, catalog refresh failure, atomic runtime replacement, and active-only routing behavior.
- **Simplicity & Minimalism**: PASS. The chosen direction introduces the minimum architecture needed to separate desired from applied truth, reuses the existing manager/host/resolver/notification seams where practical, and avoids speculative optional-feature modeling.

**Post-Design Re-check**: PASS. The design artifacts keep the implementation centered on a small set of runtime-state records, a refreshable catalog snapshot, an atomic per-shell candidate commit flow, and one public status-inspection seam rather than a broad new abstraction graph.

## Project Structure

### Documentation (this feature)

```text
specs/005-deferred-shell-activation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── shell-runtime-reconciliation-contract.md
│   └── runtime-feature-catalog-contract.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   ├── Configuration/
│   │   └── IShellSettingsProvider.cs
│   ├── Features/
│   │   └── IFeatureAssemblyProvider.cs
│   └── Management/
│       ├── IShellManager.cs
│       ├── IShellRuntimeStateAccessor.cs           # likely new public read contract
│       ├── ShellReconciliationOutcome.cs           # likely new public outcome vocabulary
│       └── ShellRuntimeStatus.cs                   # likely new public desired-vs-applied snapshot
├── CShells/
│   ├── Configuration/
│   │   ├── IShellSettingsCache.cs
│   │   ├── ShellSettingsCache.cs
│   │   └── ShellSettingsCacheInitializer.cs
│   ├── Features/
│   │   ├── FeatureDiscovery.cs
│   │   └── RuntimeFeatureCatalog.cs               # likely new internal refreshable catalog snapshot
│   ├── Hosting/
│   │   ├── DefaultShellHost.cs
│   │   ├── IShellHost.cs
│   │   ├── ShellContext.cs
│   │   └── ShellStartupHostedService.cs
│   ├── Management/
│   │   └── DefaultShellManager.cs
│   ├── Notifications/
│   │   ├── ShellActivated.cs
│   │   ├── ShellAdded.cs
│   │   ├── ShellDeactivating.cs
│   │   ├── ShellReloaded.cs
│   │   ├── ShellReloading.cs
│   │   ├── ShellRemoved.cs
│   │   ├── ShellsReloaded.cs
│   │   └── ShellUpdated.cs
│   └── Resolution/
│       └── DefaultShellResolverStrategy.cs
└── CShells.AspNetCore/
    ├── Notifications/
    │   └── ShellEndpointRegistrationHandler.cs
    ├── Resolution/
    │   └── WebRoutingShellResolver.cs
    └── Routing/
        └── DynamicShellEndpointDataSource.cs

tests/
├── CShells.Tests/
│   ├── Unit/
│   │   ├── Features/
│   │   ├── Hosting/
│   │   ├── Management/
│   │   └── Resolution/
│   └── Integration/
│       ├── DefaultShellHost/
│       ├── Management/
│       └── AspNetCore/
└── CShells.Tests.EndToEnd/

docs/
├── shell-lifecycle.md
└── shell-resolution.md

wiki/
├── Runtime-Shell-Management.md
├── Shell-Lifecycle.md
└── Shell-Resolution.md
```

**Structure Decision**: Keep the runtime orchestration work inside the existing `CShells` implementation projects instead of inventing a separate reconciliation package. The internal redesign centers on `DefaultShellHost`, `DefaultShellManager`, the in-memory shell settings/runtime caches, and a new refreshable catalog snapshot near feature discovery. The only planned new public surface is a minimal operator-facing desired-vs-applied state inspection contract in `CShells.Abstractions/Management`, plus any supporting public records or enums it returns. `ShellContext` gains a `MissingFeatures` property so shell-internal code can inspect what was not loaded. ASP.NET Core changes remain focused on routing and endpoint registration so only committed applied runtimes are visible to the web layer. The `DeferredDueToMissingFeatures` outcome is replaced by `ActiveWithMissingFeatures`; the `MarkDeferred` method on the state store is removed and replaced by committing the partial runtime directly.

## Complexity Tracking

No constitution violations or justified complexity exceptions were identified during planning.
