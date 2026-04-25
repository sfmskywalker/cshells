# Implementation Plan: Single-Provider Blueprint Simplification

**Branch**: `008-single-provider-simplification` | **Date**: 2026-04-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/008-single-provider-simplification/spec.md`

## Summary

Retire the multi-provider composition machinery introduced in feature `007`
(`CompositeShellBlueprintProvider`, `CompositeCursorCodec`,
`CompositeProviderOptions`, `DuplicateBlueprintException`) in favor of a strict
single-provider model. The registry depends on exactly one
`IShellBlueprintProvider` resolved from DI: the built-in
`InMemoryShellBlueprintProvider` by default (which `AddShell(...)` populates), or
an explicit external provider registered via `AddBlueprintProvider(factory)`.
Mixing the two is disallowed at composition time with a teaching-grade error
message.

This is a **deletion-heavy refactor**: ~350 lines of production code removed plus
two test files (~250 lines). The public-API surface that real consumers use
(`AddShell`, `AddBlueprint`, `AddBlueprintProvider`, `WithConfigurationProvider`,
`WithFluentStorageBlueprints`, `PreWarmShells`) is unchanged in shape; only the
"mix `AddShell` with `WithXxx`" combination becomes a fail-fast error rather than
silently working through the composite. `IShellBlueprintProvider` remains the
open-ended extension point: any first- or third-party implementation registers
through the same `AddBlueprintProvider` seam.

## Technical Context

**Language/Version**: C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Hosting.Abstractions`, `Ardalis.GuardClauses`; no new third-party packages
**Storage**: N/A — in-memory registry; one provider owns its own storage
**Testing**: xUnit 2.x with `Assert.*`; existing 007 single-source tests carried forward; composite/cursor-codec tests deleted
**Target Platform**: .NET server / generic host
**Performance Goals**: Removing the composite removes a layer of indirection on every `GetAsync` lookup. Hot-path cost goes from O(N providers) probes (Debug duplicate-detection) or 1 probe + composite overhead (Release) to a single direct provider call.
**Constraints**: Composition-time guard (FR-005, FR-006) MUST fire before any HTTP traffic — enforced at first `IShellRegistry` resolution, which the startup hosted service guarantees during `StartAsync`.
**Scale/Scope**: Same as 007 — up to 100k+ blueprints in a catalogue, a few thousand active generations on one host. Provider extensibility is unchanged: any `IShellBlueprintProvider` implementation works.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Abstraction-First** | ✅ PASS | `IShellBlueprintProvider` remains in `CShells.Abstractions/Lifecycle/`. The simplification only deletes implementation types and one exception; no new public abstractions. |
| **II. Feature Modularity** | ✅ PASS | Feature model unchanged; `IShellFeature.ConfigureServices` untouched. |
| **III. Modern C# Style** | ✅ PASS | Edits preserve nullable, file-scoped namespaces, primary constructors, expression-bodied members, collection expressions. |
| **IV. Explicit Error Handling** | ✅ PASS | The fail-fast guard (FR-005) raises a structured `InvalidOperationException` with actionable guidance — strengthens the principle. `DuplicateBlueprintException` is removed because the error condition no longer exists, not because we're suppressing diagnostics. |
| **V. Test Coverage** | ✅ PASS | Single-source coverage from 007 preserved (`InMemoryShellBlueprintProviderTests`, `ConfigurationShellBlueprintProviderTests`, `ShellRegistryGetOrActivateTests`, etc.). Two new tests cover the fail-fast guard scenarios; one new test demonstrates a third-party stub provider (SC-008). |
| **VI. Simplicity & Minimalism** | ✅ PASS | This feature *embodies* simplicity. ~350 LOC of speculative composition machinery deleted; one fewer exception type; one fewer DI binding pattern (no more `IEnumerable<IShellBlueprintProvider>` wired into a composite). |
| **VII. Lifecycle & Concurrency** | ✅ PASS | The composite never owned any lifecycle state — its removal touches only blueprint sourcing. Per-name semaphore, CAS state machine, drain machinery are untouched. |

**No constitution violations.** This refactor strictly reduces surface area and complexity.

## Project Structure

### Documentation (this feature)

```text
specs/008-single-provider-simplification/
├── plan.md                                    ← this file
├── research.md                                ← Phase 0
├── data-model.md                              ← Phase 1 (delta from 007)
├── quickstart.md                              ← Phase 1
├── contracts/                                 ← Phase 1
│   └── IShellRegistry.md                      (delta: ctor takes single provider)
├── checklists/
│   └── requirements.md                        ← /speckit.specify output
└── tasks.md                                   ← Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/CShells.Abstractions/
└── Lifecycle/
    └── DuplicateBlueprintException.cs         (DELETED)

src/CShells/
├── Lifecycle/
│   ├── ShellRegistry.cs                       (MODIFIED — ctor takes IShellBlueprintProvider directly; ShouldWrapAsUnavailable simplified to drop the DuplicateBlueprintException exclusion)
│   └── Providers/
│       ├── CompositeShellBlueprintProvider.cs (DELETED — also contains CompositeProviderOptions inline)
│       └── CompositeCursorCodec.cs            (DELETED)
└── DependencyInjection/
    ├── CShellsBuilder.cs                      (MODIFIED — track external-provider registration count; AddBlueprintProvider second call throws; AddShell + AddBlueprintProvider mix raises FR-005 error)
    └── ServiceCollectionExtensions.cs         (MODIFIED — drop CompositeShellBlueprintProvider DI wiring; register IShellBlueprintProvider directly from either the in-memory provider OR the single external factory)

tests/CShells.Tests/
├── Unit/Lifecycle/Providers/
│   └── CompositeCursorCodecTests.cs           (DELETED)
├── Integration/Lifecycle/
│   └── CompositeShellBlueprintProviderTests.cs (DELETED — composite gone; duplicate-detection scenarios moot)
├── Unit/Lifecycle/
│   └── ExceptionMessageTests.cs               (MODIFIED — drop DuplicateBlueprintException assertions; keep BlueprintNotMutable + ShellBlueprintUnavailable)
└── Integration/Lifecycle/
    └── ShellRegistryGuardTests.cs             (NEW — covers FR-005, FR-006 fail-fast scenarios + SC-008 third-party provider)
```

**Structure Decision**: pure deletion + small modifications to two DI files plus
the registry constructor. No new directories, no new files in production code
(one new integration test file). The simplification touches the same `Lifecycle/`
and `DependencyInjection/` namespaces as 007 — no architectural reshape.

## Complexity Tracking

> No constitution violations to justify. This feature *removes* complexity it
> previously added in 007. The composite was a speculative-flexibility cost paid
> per-deployment for an extensibility no real user requested.
