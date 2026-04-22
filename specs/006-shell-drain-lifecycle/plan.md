# Implementation Plan: Shell Draining & Disposal Lifecycle

**Branch**: `006-shell-drain-lifecycle` | **Date**: 2026-04-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-shell-drain-lifecycle/spec.md`

## Summary

Introduce a named, versioned shell lifecycle model with explicit monotonic state transitions
(Initializing → Active → Deactivating → Draining → Drained → Disposed), cooperative drain handlers,
configurable drain timeout policies, and observable state-change events.
The implementation extends `CShells.Abstractions` with a new `Lifecycle/` namespace for all public
contracts and adds corresponding implementations in `CShells/Lifecycle/`. The existing `ShellId`
is extended to carry both `Name` and `Version` (a justified breaking change per Principle VI).
`IShellRegistry` becomes the authoritative API for creating, promoting, and draining shells;
the drain phase ensures host-registered handlers complete in-flight work before the shell's
`IServiceProvider` is disposed.

## Technical Context

**Language/Version**: C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Ardalis.GuardClauses`; all pinned via `Directory.Packages.props`
**Storage**: N/A — in-memory registry (`ConcurrentDictionary` + per-name `SemaphoreSlim`)
**Testing**: xUnit 2.x with `Assert.*`; unit tests mirror `src/` structure; integration tests in `Integration/`
**Target Platform**: .NET server / generic host
**Project Type**: Library (NuGet packages)
**Performance Goals**: SC-005 — drain completes within T + G seconds (G = 3 s default grace period) under all built-in policy types
**Constraints**: Thread-safe under concurrent promote/drain; monotonic state machine; no `lock()` around async paths (Principle VII)
**Scale/Scope**: 0–50 drain handlers per shell (SC-002); registry holding O(tens) of active shells at any time

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Abstraction-First** | ✅ PASS | All new public contracts go in `CShells.Abstractions/Lifecycle/`; implementations in `CShells/Lifecycle/` |
| **II. Feature Modularity** | ✅ PASS | Drain handlers registered via `IShellFeature.ConfigureServices`; lifecycle logger auto-registers through DI setup |
| **III. Modern C# Style** | ✅ PASS | Nullable enabled, file-scoped namespaces, primary constructors, `Guard.Against.*`, expression-bodied members, collection expressions |
| **IV. Explicit Error Handling** | ✅ PASS | Duplicate `ShellId` → descriptive exception; promote on non-Active shell → descriptive exception; handler exceptions captured in `DrainHandlerResult`, never swallowed silently |
| **V. Test Coverage** | ✅ PASS | Unit tests for state machine and each policy type; integration tests for concurrent promote/drain/replace scenarios |
| **VI. Simplicity** | ✅ PASS | Three policy types cover all spec requirements exactly; no speculative extensibility |
| **VII. Lifecycle & Concurrency** | ✅ PASS | Per-name `SemaphoreSlim(1,1)` for promote serialization; `Interlocked.CompareExchange` for idempotent drain creation; subscriber exceptions caught+logged per FR-021; monotonic state via atomic CAS |

**Breaking change justified**: `ShellId` is extended to carry `Version` alongside `Name`. The spec defines `ShellId` as `(name, version)` throughout (FR-010, FR-013, FR-019). Principle VI explicitly permits breaking changes when they improve API clarity.

## Project Structure

### Documentation (this feature)

```text
specs/006-shell-drain-lifecycle/
├── plan.md              ← this file
├── research.md          ← Phase 0
├── data-model.md        ← Phase 1
├── quickstart.md        ← Phase 1
├── contracts/           ← Phase 1
│   ├── IShellRegistry.md
│   ├── IShell.md
│   ├── IDrainHandler.md
│   ├── IDrainPolicy.md
│   └── IDrainOperation.md
└── tasks.md             ← Phase 2 (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   └── Lifecycle/
│       ├── IShell.cs                      (shell handle: state + service provider)
│       ├── IShellRegistry.cs              (create / promote / drain / replace)
│       ├── IShellLifecycleSubscriber.cs   (per-shell and global event callback)
│       ├── ShellLifecycleState.cs         (enum: Initializing…Disposed)
│       ├── ShellDescriptor.cs             (immutable identity + metadata record)
│       ├── IDrainHandler.cs               (host-registered cooperative shutdown hook)
│       ├── IDrainOperation.cs             (in-flight drain handle: wait / force)
│       ├── IDrainPolicy.cs                (timeout strategy interface)
│       ├── IDrainExtensionHandle.cs       (handler → policy deadline extension)
│       ├── DrainResult.cs                 (structured drain completion record)
│       └── DrainHandlerResult.cs          (per-handler outcome)
│
└── CShells/
    └── Lifecycle/
        ├── Shell.cs                        (IShell implementation, state machine)
        ├── ShellRegistry.cs                (IShellRegistry implementation)
        ├── DrainOperation.cs               (IDrainOperation implementation)
        ├── Policies/
        │   ├── FixedTimeoutDrainPolicy.cs
        │   ├── ExtensibleTimeoutDrainPolicy.cs
        │   └── UnboundedDrainPolicy.cs
        └── ShellLifecycleLogger.cs         (ILogger-backed IShellLifecycleSubscriber)

tests/
├── CShells.Tests/
│   ├── Unit/
│   │   └── Lifecycle/
│   │       ├── ShellStateMachineTests.cs
│   │       ├── DrainOperationTests.cs
│   │       ├── FixedTimeoutPolicyTests.cs
│   │       ├── ExtensibleTimeoutPolicyTests.cs
│   │       └── UnboundedPolicyTests.cs
│   └── Integration/
│       └── Lifecycle/
│           ├── ShellRegistryDrainTests.cs
│           ├── ShellRegistryReplaceTests.cs
│           └── ShellRegistryConcurrencyTests.cs
```

**Structure Decision**: Extend existing `CShells.Abstractions` and `CShells` projects with a
`Lifecycle/` sub-namespace. No new projects are introduced — drain lifecycle is a core library
capability, not a separate adapter or provider.

## Complexity Tracking

> No constitution violations to justify.
