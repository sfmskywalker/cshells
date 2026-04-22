# Implementation Plan: Shell Generations, Reload & Disposal Lifecycle

**Branch**: `006-shell-drain-lifecycle` | **Date**: 2026-04-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-shell-drain-lifecycle/spec.md`

## Summary

Overhaul the core CShells lifecycle surface. Introduce blueprint-driven shells with
library-assigned monotonic **generations**, a CAS-based lifecycle state machine
(`Initializing → Active → Deactivating → Draining → Drained → Disposed`), per-shell
initializers, cooperative drain handlers preceded by a built-in scope-wait phase,
configurable drain timeout policies, and observable state-change events. The host registers
one blueprint per shell name (fluent `ShellBuilder` or configuration-backed); the library
composes a fresh `ShellSettings` from the blueprint on every activation and reload, stamps a
new generation, runs initializers, promotes the generation to `Active`, and drains the
previous generation in the background.

This is a **clean overhaul**. The legacy `IShellHost` / `IShellManager` / `ShellContext` /
`IShellSettingsProvider` / `IShellActivatedHandler` / `IShellDeactivatingHandler` surface is
deleted entirely and replaced by `IShellRegistry` + `IShell` + `IShellBlueprint` +
`IShellInitializer` + `IDrainHandler` + `IShellScope`. Every downstream integration
(`CShells.AspNetCore`, `CShells.FastEndpoints`, `CShells.AspNetCore.Testing`,
`CShells.Providers.FluentStorage`, samples, tests) is migrated in-place; no legacy surface
remains after this feature ships.

`ShellId` stays name-only. Generation lives on `IShell.Descriptor.Generation` only — no
breaking change to the identity type, no sentinel generations, no compat shims.

## Technical Context

**Language/Version**: C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Hosting.Abstractions`, `Ardalis.GuardClauses`; all pinned via `Directory.Packages.props`
**Storage**: N/A — in-memory registry (`ConcurrentDictionary` + per-name `SemaphoreSlim`)
**Testing**: xUnit 2.x with `Assert.*`; unit tests mirror `src/` structure; integration tests in `Integration/`
**Target Platform**: .NET server / generic host
**Performance Goals**: SC-007 — drain completes within T + G seconds (G = 3 s default grace period) under all built-in policy types
**Constraints**: Thread-safe under concurrent reload/drain; monotonic state machine; no `lock()` around async paths (Principle VII); per-name serialization of reloads; scope-tracking counters via `Interlocked.*`
**Scale/Scope**: 0–50 drain handlers per shell (SC-004); registry holding O(tens) of active shells at any time; multiple generations of the same name may coexist (one `Active` + any number draining); O(thousands) of concurrent in-flight scope handles

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Abstraction-First** | ✅ PASS | Every new public contract lives in `CShells.Abstractions/Lifecycle/`; implementations in `CShells/Lifecycle/` |
| **II. Feature Modularity** | ✅ PASS | Initializers and drain handlers registered via `IShellFeature.ConfigureServices`; lifecycle logger auto-registers through DI setup; blueprints compose from the same `ShellSettings` pipeline features already use |
| **III. Modern C# Style** | ✅ PASS | Nullable enabled, file-scoped namespaces, primary constructors, `Guard.Against.*`, expression-bodied members, collection expressions |
| **IV. Explicit Error Handling** | ✅ PASS | Duplicate blueprint → descriptive exception; reload without blueprint → descriptive exception; composition / build / initializer exceptions propagate without leaving partial generations; handler exceptions captured in `DrainHandlerResult`, never swallowed silently |
| **V. Test Coverage** | ✅ PASS | Unit tests for state machine, each policy type, blueprint composition, initializer ordering, scope-wait phase; integration tests for activate / reload / reload-all / concurrent reload / drain / shutdown / AspNetCore middleware |
| **VI. Simplicity** | ✅ PASS | Three policy types; one blueprint method; scope-wait as a hard-coded phase rather than a pluggable extensibility point; legacy surface removed outright rather than kept alongside as a compat shim |
| **VII. Lifecycle & Concurrency** | ✅ PASS | Per-name `SemaphoreSlim(1,1)` for reload serialization; `Interlocked.CompareExchange` for idempotent drain creation; `Interlocked.Increment/Decrement` for scope counter; subscriber exceptions caught+logged; monotonic state via atomic CAS |

**Breaking changes justified**: Entire `Management/`, `Hosting/IShell*`, `Hosting/*HostedService`,
`Configuration/*SettingsProvider*`, `Configuration/ShellSettingsCache*`, and handler interfaces
from `Hosting/` are removed (FR-038). Principle VI permits breaking changes that improve API
clarity; the overhaul collapses five overlapping concepts (host, manager, context, settings
provider, handler) into one coherent lifecycle model. All downstream projects in this repo are
migrated in the same PR (FR-039), so no external consumer is left on a dangling legacy API.

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
│   ├── IShellBlueprint.md
│   ├── IShellInitializer.md
│   ├── IDrainHandler.md
│   ├── IDrainPolicy.md
│   └── IDrainOperation.md
└── tasks.md             ← Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── CShells.Abstractions/
│   ├── ShellId.cs                               (unchanged — name only)
│   ├── ShellSettings.cs                         (unchanged — generation-unaware)
│   ├── ShellSettingsExtensions.cs               (unchanged)
│   ├── Guard.cs                                 (unchanged)
│   ├── Features/                                (unchanged — feature model orthogonal)
│   └── Lifecycle/                               (NEW)
│       ├── IShell.cs
│       ├── IShellBlueprint.cs
│       ├── IShellRegistry.cs
│       ├── IShellInitializer.cs
│       ├── IShellScope.cs
│       ├── IShellLifecycleSubscriber.cs
│       ├── ShellLifecycleState.cs
│       ├── ShellDescriptor.cs
│       ├── ReloadResult.cs
│       ├── IDrainHandler.cs
│       ├── IDrainOperation.cs
│       ├── IDrainPolicy.cs
│       ├── IDrainExtensionHandle.cs
│       ├── DrainResult.cs
│       └── DrainHandlerResult.cs
│   │
│   ├── Management/                              (DELETED — every file)
│   ├── Hosting/IShellActivatedHandler.cs        (DELETED)
│   ├── Hosting/IShellDeactivatingHandler.cs     (DELETED)
│   ├── Hosting/ShellHandlerOrderAttribute.cs    (DELETED)
│   └── Configuration/IShellSettingsProvider.cs  (DELETED)
│
└── CShells/
    ├── Configuration/
    │   ├── ShellBuilder.cs                      (kept; used by DelegateShellBlueprint)
    │   ├── ShellConfig.cs                       (kept)
    │   ├── ShellConfiguration.cs                (kept — the `IConfiguration` impl registered in each shell's DI container, merging shell-specific and root config; consumed by `ShellBuilder`, `ConfigurationHelper`, and the new `ShellRegistry.ActivateAsync` provider-build step)
    │   ├── FeatureEntry.cs                      (kept)
    │   ├── FeatureEntryJsonConverter.cs         (kept)
    │   ├── FeatureEntryListJsonConverter.cs     (kept)
    │   ├── ConfigurationHelper.cs               (kept — may simplify after SettingsProvider is gone)
    │   ├── CShellsOptions.cs                    (kept)
    │   ├── CShellsBuilderExtensions.cs          (rewritten — AddShell / ConfigureDrainPolicy / ConfigureGracePeriod)
    │   │
    │   ├── CompositeShellSettingsProvider.cs    (DELETED)
    │   ├── ConfigurationShellSettingsProvider.cs (DELETED)
    │   ├── ConfiguringShellSettingsProvider.cs  (DELETED)
    │   ├── InMemoryShellSettingsProvider.cs     (DELETED)
    │   ├── MutableInMemoryShellSettingsProvider.cs (DELETED)
    │   ├── IShellSettingsCache.cs               (DELETED)
    │   ├── ShellSettingsCache.cs                (DELETED)
    │   ├── ShellSettingsCacheInitializer.cs     (DELETED)
    │   └── ShellSettingsFactory.cs              (DELETED)
    │
    ├── Hosting/
    │   ├── CShellsStartupHostedService.cs       (NEW — startup auto-activation + shutdown drain)
    │   ├── IShellServiceExclusionProvider.cs    (kept)
    │   ├── IShellServiceExclusionRegistry.cs    (kept)
    │   ├── ShellServiceExclusionRegistry.cs     (kept)
    │   ├── DefaultShellServiceExclusionProvider.cs (kept)
    │   │
    │   ├── IShellHost.cs                        (DELETED)
    │   ├── DefaultShellHost.cs                  (DELETED — 836 lines)
    │   ├── IShellHostInitializer.cs             (DELETED)
    │   ├── ShellContext.cs                      (DELETED)
    │   ├── IShellContextScope.cs                (DELETED)
    │   ├── IShellContextScopeFactory.cs         (DELETED)
    │   ├── DefaultShellContextScopeFactory.cs   (DELETED)
    │   ├── ShellContextScopeHandle.cs           (DELETED)
    │   ├── ShellCandidateBuildResult.cs         (DELETED)
    │   ├── ShellStartupHostedService.cs         (DELETED)
    │   └── ShellFeatureInitializationHostedService.cs (DELETED)
    │
    ├── Management/                              (DELETED — every file)
    │
    ├── Lifecycle/                               (NEW)
    │   ├── Shell.cs                             (IShell impl; CAS state machine; scope counter)
    │   ├── ShellScope.cs                        (IShellScope impl)
    │   ├── ShellRegistry.cs                     (IShellRegistry impl; per-name NameSlot; blueprint store)
    │   ├── ShellLifecycleLogger.cs              (auto-registered ILogger subscriber)
    │   ├── DrainOperation.cs                    (IDrainOperation impl; phases 1/2/3)
    │   ├── Blueprints/
    │   │   ├── DelegateShellBlueprint.cs
    │   │   └── ConfigurationShellBlueprint.cs
    │   └── Policies/
    │       ├── FixedTimeoutDrainPolicy.cs
    │       ├── ExtensibleTimeoutDrainPolicy.cs
    │       └── UnboundedDrainPolicy.cs
    │
    └── DependencyInjection/
        ├── CShellsBuilder.cs                    (kept; surface evolves)
        ├── CShellsBuilderExtensions.cs          (kept; rewritten to expose AddShell/ConfigureDrainPolicy/ConfigureGracePeriod)
        ├── ServiceCollectionExtensions.cs       (rewritten — no more SettingsProvider/ShellHost wiring; registers Registry + Logger + StartupHostedService)
        ├── IRootServiceCollectionAccessor.cs    (kept)
        └── RootServiceCollectionAccessor.cs     (kept)

src/CShells.AspNetCore/ (migrated in-place)
├── Middleware/ShellMiddleware.cs                (rewired: IShellRegistry + IShell.BeginScope)
├── Resolution/WebRoutingShellResolver.cs        (rewired: registry.GetBlueprintNames + registry.GetActive)
├── Resolution/FixedShellResolver.cs             (rewired)
├── Resolution/DefaultShellResolverStrategy.cs   (rewired if present)
├── Routing/ShellEndpointMetadata.cs             (kept; carries ShellId name only)
├── Hosting/AspNetCoreShellServiceExclusionProvider.cs (unchanged)
└── Extensions/                                  (migrated)

src/CShells.FastEndpoints/ (migrated in-place)
└── Features/                                    (rewired to IShell where needed)

src/CShells.AspNetCore.Testing/ (migrated in-place)

src/CShells.Providers.FluentStorage/ (migrated in-place — no longer implements IShellSettingsProvider; implements IShellBlueprint or exposes a blueprint factory)

samples/
├── CShells.Workbench/                           (migrated; README updated; demo worker shows ReloadAsync rollover)
└── CShells.Workbench.Features/                  (updated; sample IShellInitializer + IDrainHandler)

tests/
└── CShells.Tests/
    ├── Unit/
    │   └── Lifecycle/
    │       ├── ShellStateMachineTests.cs
    │       ├── ShellScopeTests.cs
    │       ├── DrainOperationTests.cs
    │       ├── FixedTimeoutPolicyTests.cs
    │       ├── ExtensibleTimeoutPolicyTests.cs
    │       ├── UnboundedPolicyTests.cs
    │       └── Blueprints/
    │           ├── DelegateShellBlueprintTests.cs
    │           └── ConfigurationShellBlueprintTests.cs
    └── Integration/
        └── Lifecycle/
            ├── ShellRegistryActivateTests.cs
            ├── ShellRegistryReloadTests.cs
            ├── ShellRegistryReloadAllTests.cs
            ├── ShellRegistryDrainTests.cs
            ├── ShellRegistryScopeWaitTests.cs
            ├── ShellRegistryInitializerTests.cs
            ├── ShellRegistryConcurrencyTests.cs
            └── ShellRegistryShutdownTests.cs
```

**Structure Decision**: Extend `CShells.Abstractions` and `CShells` with a new `Lifecycle/`
namespace and delete the legacy hosting/management/settings-provider surface in the same PR.
Every downstream integration is migrated in-place; no legacy types remain after this feature
ships. The scope of deletion (roughly 2,500 lines of `src/CShells` plus adjacent abstractions)
is substantial but self-contained — the new surface is strictly smaller and conceptually
simpler.

## Complexity Tracking

> No constitution violations to justify. The deletion of legacy types is in the spirit of
> Principle VI (simplicity) — one lifecycle model, one identity model, one settings source,
> one registry.
