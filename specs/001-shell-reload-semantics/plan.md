# Implementation Plan: Shell Reload Semantics

**Branch**: `001-shell-reload-semantics` | **Date**: 2026-03-08 | **Spec**: `/Users/sipke/Projects/ValenceWorks/cshells/main/specs/001-shell-reload-semantics/spec.md`
**Input**: Feature specification from `/specs/001-shell-reload-semantics/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add strict single-shell reload support to the runtime management API, add provider-level single-shell lookup, and correct stale shell-context behavior for both single-shell and full reload flows. The implementation will move the affected public interfaces into `CShells.Abstractions`, keep host cache invalidation as an internal framework seam, and preserve current lifecycle events while adding dedicated reload lifecycle notifications.

## Technical Context

**Language/Version**: C# 14 on .NET 10 with multi-targeted source projects (`net8.0;net9.0;net10.0`)  
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, ASP.NET Core integration packages in adjacent projects, internal notification pipeline in `CShells.Notifications`  
**Storage**: N/A at the feature level; shell definitions come from configurable providers (configuration, FluentStorage, in-memory, composite provider)  
**Testing**: xUnit with `Assert.*`, unit tests in `tests/CShells.Tests/Unit/`, integration tests in `tests/CShells.Tests/Integration/`  
**Target Platform**: Cross-platform .NET class library consumed by ASP.NET Core applications  
**Project Type**: Multi-project framework/library with documentation and tests  
**Performance Goals**: Single-shell reload should avoid full provider enumeration; reloads should invalidate only affected cached shell contexts and preserve lazy rebuilding behavior  
**Constraints**: Preserve current lifecycle notifications where still required by the spec, keep XML documentation complete, and avoid broad eager rebuilds of all shell service providers  
**Scale/Scope**: Changes span `CShells.Abstractions`, the core `CShells` library, provider implementations, notification contracts, runtime management docs/wiki, and focused unit/integration coverage

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Abstraction-First Architecture**: PASS. The feature moves the affected public interfaces (`IShellManager`, `IShellSettingsProvider`) into `CShells.Abstractions`. Host invalidation remains an internal implementation seam, and reload notification records remain framework-owned message types in `CShells`, both of which are allowed under the amended constitution.
- **Feature Modularity**: PASS. Changes are isolated to runtime management, provider lookup, host cache invalidation, notification types, and documentation; no cross-feature coupling beyond existing management/hosting seams.
- **Modern C# Style**: PASS. Planned changes fit the repository’s file-scoped namespaces, nullable annotations, explicit modifiers, and minimal API extension style.
- **Explicit Error Handling**: PASS. Strict single-shell reload explicitly fails when provider lookup returns no shell and avoids silent mutation of stale runtime state.
- **Test Coverage**: PASS. Unit coverage is required for manager/provider/notification semantics and integration coverage is required for stale-context invalidation and reconciliation flows.
- **Simplicity & Minimalism**: PASS. The design introduces a targeted provider lookup and host invalidation seam rather than adding eager rebuild orchestration or speculative caching abstractions.

**Post-Design Re-check**: PASS. Research and design artifacts preserve the same architecture, testing, and simplicity constraints without introducing constitution violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-shell-reload-semantics/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── shell-reload-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   ├── Configuration/
│   │   └── IShellSettingsProvider.cs
│   └── Management/
│       └── IShellManager.cs
├── CShells/
│   ├── Configuration/
│   │   ├── CompositeShellSettingsProvider.cs
│   │   ├── ConfigurationShellSettingsProvider.cs
│   │   ├── InMemoryShellSettingsProvider.cs
│   │   └── MutableInMemoryShellSettingsProvider.cs
│   ├── Hosting/
│   │   ├── IShellHost.cs
│   │   └── DefaultShellHost.cs
│   ├── Management/
│   │   └── DefaultShellManager.cs
│   └── Notifications/
│       ├── INotification.cs
│       ├── INotificationPublisher.cs
│       ├── ShellActivated.cs
│       ├── ShellDeactivating.cs
│       ├── ShellAdded.cs
│       ├── ShellReloading.cs
│       ├── ShellReloaded.cs
│       ├── ShellRemoved.cs
│       ├── ShellUpdated.cs
│       └── ShellsReloaded.cs
├── CShells.AspNetCore/
│   └── Notifications/
└── CShells.Providers.FluentStorage/

tests/
├── CShells.Tests/
│   ├── Unit/
│   │   ├── Configuration/
│   │   └── Management/
│   └── Integration/
│       ├── DefaultShellHost/
│       └── Configuration/
└── CShells.Tests.EndToEnd/

docs/
└── multiple-shell-providers.md

wiki/
├── Runtime-Shell-Management.md
└── Multiple-Shell-Providers.md
```

**Structure Decision**: Move the public interface changes into `src/CShells.Abstractions/Configuration` and `src/CShells.Abstractions/Management` to comply with the project constitution. Keep host cache invalidation inside `src/CShells/Hosting` as an internal framework seam rather than expanding the public host contract. Reload notification records remain in `src/CShells/Notifications` as framework-owned runtime messages covered by the constitution's notification-record exception. Documentation updates stay in the existing `docs/` and `wiki/` files.

## Complexity Tracking

No constitution violations or justified complexity exceptions were identified during planning.

