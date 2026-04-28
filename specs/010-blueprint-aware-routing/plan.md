# Implementation Plan: Blueprint-Aware Path Routing

**Branch**: `010-blueprint-aware-routing` | **Date**: 2026-04-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/010-blueprint-aware-routing/spec.md`

## Summary

Restore the lazy-activation guarantee that feature `007` introduced but the built-in `WebRoutingShellResolver` did not deliver. Today the resolver iterates `IShellRegistry.GetActiveShells()` only, so a blueprint that has not been pre-warmed is invisible to routing — the request returns `404` with no log output, and `ShellMiddleware`'s `GetOrActivateAsync` call is unreachable. After this feature, routing consults a new blueprint-backed `IShellRouteIndex` and returns a `ShellId` for any blueprint whose `WebRouting:*` configuration matches the request, regardless of activation state. `ShellMiddleware`'s existing `GetOrActivateAsync` call then materialises the shell on demand — exactly the lazy-activation contract `007`'s spec promised.

This change also fixes the post-reload 404 (`SC-002`): `ReloadAsync` drains the active generation but does not auto-activate the next one. Today this leaves the registry empty and the resolver blind. With the route index, the next matching request triggers lazy re-activation through the same path.

`PreWarmShells` is preserved unchanged as a latency hint and remains useful for hosts that want to amortise build cost at startup.

This is a **focused additive change** with one targeted breaking API delta:

- **NEW**: `IShellRouteIndex` (and its supporting types) in `CShells.AspNetCore.Abstractions`. Implementation `DefaultShellRouteIndex` in `CShells.AspNetCore`.
- **MODIFIED (breaking)**: `IShellResolverStrategy.Resolve` becomes async (`ValueTask<ShellId?> ResolveAsync(...)`) because the route index is asynchronous. `WebRoutingShellResolver` and `DefaultShellResolverStrategy` migrate to the new signature. `ShellMiddleware` awaits the resolver. Custom strategies in third-party code need a one-line migration.
- **UNCHANGED**: `IShellRegistry`, `IShellBlueprintProvider`, `IShellBlueprintManager`, `IShellLifecycleSubscriber`, `ShellEndpointRegistrationHandler`, `WebRoutingShellResolverOptions`, the shell configuration shape (`Configuration:WebRouting:{Path,Host,HeaderName,ClaimKey}`), and `CShellsBuilder.PreWarmShells`.

The Workbench sample's `Program.cs` drops any `PreWarmShells` call. The downstream consumer `Elsa.ModularServer.Web` (out-of-tree) similarly drops its `.PreWarmShells("Default")` call when adopting this version.

## Technical Context

**Language/Version**: C# 14 / .NET 10 — multi-target `net8.0;net9.0;net10.0` for library projects; `net10.0` for tests
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Ardalis.GuardClauses`; no new third-party packages. Reuses existing `IShellBlueprintProvider`, `IShellLifecycleSubscriber`, `INotificationHandler<T>`.
**Storage**: N/A — the route index is an in-memory snapshot rebuilt from the provider catalogue on demand and on lifecycle events.
**Testing**: xUnit 2.x with `Assert.*`; unit tests mirror `src/` structure; integration tests in `Integration/AspNetCore/`; end-to-end tests in `tests/CShells.Tests.EndToEnd/`.
**Target Platform**: ASP.NET Core (`Microsoft.AspNetCore.App` framework reference). Pure `CShells` (no AspNetCore) consumers are unaffected; the index lives in `CShells.AspNetCore.Abstractions`.
**Performance Goals**:
- SC-003 — startup time delta ≤ ±50 ms for one-shell hosts vs. baseline (no eager catalogue scan at startup).
- SC-004 — startup time delta ≤ ±50 ms between 10-blueprint and 100,000-blueprint hosts (the index does NOT eagerly enumerate the catalogue at startup).
- Hot-path lookup for path-by-name routing: ≤ one `IShellBlueprintProvider.GetAsync(name)` call per request.
- Hot-path lookup for host/header/claim/root-path routing: O(1) snapshot read after first-use index population.

**Constraints**:
- The route index MUST NOT eagerly enumerate the catalogue at startup (preserves `007` SC-001).
- Concurrent index reads MUST observe either the previous fully-consistent snapshot or the new fully-consistent snapshot — never a partial state.
- The resolver is on the request hot path; index lookups MUST be allocation-light and sync-completed in the steady state.
- `IShellResolverStrategy.Resolve` migrates from sync to async — this is the only breaking abstraction change. Justified per Principle VI (breaking changes acceptable when they improve API quality; the existing sync signature is the root cause of the routing-vs-async-provider impedance mismatch this feature resolves).

**Scale/Scope**: Catalogues up to 100,000+ blueprints (carry-over from `007`); index snapshot ≤ a few thousand entries in steady state per the practical bound on simultaneously-active shells; index population is incremental and tolerates provider-driven turnover at runtime.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Abstraction-First** | ✅ PASS | New public contracts (`IShellRouteIndex`, `ShellRouteCriteria`, `ShellRouteEntry`) live in `CShells.AspNetCore.Abstractions/Routing/`. Implementations (`DefaultShellRouteIndex`, `ShellRouteIndexInvalidator` lifecycle subscriber) live in `CShells.AspNetCore/Routing/`. The internal `IShellRouteIndexBuilder` policy seam stays in the implementation project (no third-party extensibility intent). |
| **II. Feature Modularity** | ✅ PASS | The route index is a framework-level routing concern, not a feature; no `IShellFeature` interactions change. The shell configuration shape (`WebRouting:*`) is unchanged so feature-config compatibility is preserved. |
| **III. Modern C# Style** | ✅ PASS | `internal sealed` on `DefaultShellRouteIndex` and `ShellRouteIndexInvalidator`. Primary constructors with `Guard.Against.Null` for DI dependencies. Records for value types (`ShellRouteCriteria`, `ShellRouteEntry`). Snapshot is an immutable `FrozenDictionary` (net8+) for O(1) lookup. File-scoped namespaces, `var`, expression-bodied members, collection expressions. |
| **IV. Explicit Error Handling** | ✅ PASS | New exception type `ShellRouteIndexUnavailableException` (carries the failing provider type + inner cause) for index-population failures observable to custom resolvers. The route index itself catches and logs provider exceptions during invalidation refresh — the previous good snapshot remains active (FR-012, FR-013). The resolver's "no match" outcome is a `null` return + a single structured log entry (FR-008), not an exception. |
| **V. Test Coverage** | ✅ PASS | Unit tests for `DefaultShellRouteIndex` (snapshot consistency, lifecycle invalidation, duplicate detection, Path-with-leading-slash detection, root-path ambiguity). Integration tests for `WebRoutingShellResolver` against a stub provider with no pre-warm (the SC-001 + SC-002 regressions). End-to-end test in `tests/CShells.Tests.EndToEnd/` simulating cold-start + reload + cold-start cycle on the Workbench sample. |
| **VI. Simplicity & Minimalism** | ✅ PASS | One new public abstraction (`IShellRouteIndex`), one default implementation, one new exception, one breaking signature change (`Resolve` → `ResolveAsync`). No compatibility shims, no parallel sync-and-async resolver pipelines. The misleading "registry remains idle until first activation" log line is rephrased rather than left in place. Backward-compat is not a constraint; the migration burden on custom resolvers is one keyword (`async`). |
| **VII. Lifecycle & Concurrency** | ✅ PASS | The route index snapshot is published via `Volatile.Write` of an immutable reference; readers use `Volatile.Read` and never observe a torn or partial state (FR-013). Concurrent index population from multiple invalidation events is serialized through a single `SemaphoreSlim(1,1)` owned by `DefaultShellRouteIndex`. The index subscribes to `ShellAdded`/`ShellRemoved`/`ShellReloaded` notifications via the existing `INotificationHandler<T>` mechanism (subscriber-isolation guarantee from feature `006` preserved — the index swallows-and-logs its own exceptions so it cannot block other subscribers). All public async APIs accept and propagate `CancellationToken`. No `lock()` around async paths. |

**Breaking changes justified**: `IShellResolverStrategy.Resolve` becomes `ResolveAsync`. The constitution (Principle VI) explicitly permits breaking changes that improve architecture or API quality. The current sync signature is the root cause of why `WebRoutingShellResolver` cannot consult an asynchronous blueprint provider on the hot path — the very mismatch this feature exists to resolve. Keeping a sync facade alongside the async one would force every implementation to choose between blocking on async work (forbidden by Principle VII) and reimplementing the index lookup. The migration cost is one keyword for any custom strategy.

## Project Structure

### Documentation (this feature)

```text
specs/010-blueprint-aware-routing/
├── plan.md                                       ← this file
├── research.md                                   ← Phase 0
├── data-model.md                                 ← Phase 1
├── quickstart.md                                 ← Phase 1
├── contracts/                                    ← Phase 1
│   ├── IShellRouteIndex.md                       (new abstraction)
│   ├── ShellRouteCriteria.md                     (new value object)
│   ├── ShellRouteEntry.md                        (new value object)
│   ├── IShellResolverStrategy.md                 (delta — sync → async)
│   └── Exceptions.md                             (one new exception type)
├── checklists/
│   └── requirements.md                           ← /speckit.specify output (placeholder)
└── tasks.md                                      ← Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/CShells.AspNetCore.Abstractions/
└── Routing/                                      (NEW namespace)
    ├── IShellRouteIndex.cs                       (NEW)
    ├── ShellRouteCriteria.cs                     (NEW — record)
    ├── ShellRouteEntry.cs                        (NEW — record)
    ├── ShellRouteMatch.cs                        (NEW — record; result of TryMatchAsync)
    └── ShellRouteIndexUnavailableException.cs    (NEW)

src/CShells.AspNetCore.Abstractions/Resolution/
└── IShellResolverStrategy.cs                     (MODIFIED — Resolve → ResolveAsync; ValueTask<ShellId?> return)

src/CShells.AspNetCore/
├── Routing/                                      (NEW namespace)
│   ├── DefaultShellRouteIndex.cs                 (NEW — internal sealed; immutable snapshot, semaphore-serialized rebuild)
│   ├── ShellRouteIndexBuilder.cs                 (NEW — internal; produces ShellRouteEntry[] from IShellBlueprint)
│   └── ShellRouteIndexInvalidator.cs             (NEW — internal; INotificationHandler<ShellAdded>/<ShellRemoved>/<ShellReloaded>; calls index.RefreshAsync)
├── Resolution/
│   ├── WebRoutingShellResolver.cs                (MODIFIED — async; consults IShellRouteIndex instead of GetActiveShells)
│   └── DefaultShellResolverStrategy.cs           (MODIFIED — async; trivial migration since it always returns "Default")
├── Middleware/
│   └── ShellMiddleware.cs                        (MODIFIED — awaits resolver.ResolveAsync(...))
├── Hosting/
│   └── CShellsStartupHostedService.cs            (MODIFIED — replace "registry remains idle until first activation" log line with accurate language)
└── Extensions/
    └── ServiceCollectionExtensions.cs            (MODIFIED — register IShellRouteIndex as singleton + invalidator handler)

src/CShells/Hosting/
└── (no changes; CShellsStartupHostedService lives under CShells.AspNetCore for this concern after 007 — confirm exact path during Phase 0 research)

samples/
└── CShells.Workbench/
    └── Program.cs                                (MODIFIED — remove PreWarmShells call if present; add a comment explaining lazy activation is on by default)

tests/CShells.Tests/
├── Unit/AspNetCore/Routing/
│   ├── DefaultShellRouteIndexTests.cs            (NEW — snapshot consistency, lifecycle invalidation, duplicate detection, Path-with-leading-slash, root-path ambiguity)
│   └── ShellRouteIndexBuilderTests.cs            (NEW — extraction of WebRouting:* from blueprint properties)
└── Integration/AspNetCore/
    ├── WebRoutingShellResolverLazyActivationTests.cs (NEW — SC-001: cold blueprint resolves on first request)
    ├── WebRoutingShellResolverPostReloadTests.cs     (NEW — SC-002: reloaded shell auto-activates on next request)
    ├── WebRoutingShellResolverScalingTests.cs        (NEW — SC-004: 100k blueprints don't enumerate at startup)
    └── WebRoutingShellResolverDiagnosticsTests.cs    (NEW — SC-005: single log entry on no-match)

tests/CShells.Tests.EndToEnd/
└── Routing/
    └── ColdStartReloadCycleTests.cs              (NEW — full WebApplicationFactory: cold start, request, reload, request)
```

**Structure Decision**: The route index is a routing-layer concern, so its abstractions live in `CShells.AspNetCore.Abstractions/Routing/` and its implementation in `CShells.AspNetCore/Routing/`. `IShellResolverStrategy` and `WebRoutingShellResolver` move to async in place — they already live in the AspNetCore project. No new packages, no new third-party dependencies, no new namespaces in `CShells.Abstractions` or `CShells` (the routing concern is HTTP-specific).

## Complexity Tracking

> **No constitution violations to justify.** The feature adds one focused public abstraction (`IShellRouteIndex`) with three supporting value types and one exception. It removes one bug (cold-blueprint 404), one bug (post-reload 404), and one misleading log line. It does NOT add a configuration knob, a builder method, a feature, a notification, or a third-party dependency.
>
> The single breaking change (`Resolve` → `ResolveAsync`) is directly motivated by the impedance mismatch between the sync resolver pipeline and the async blueprint provider. Keeping the sync signature would require either sync-over-async (forbidden by Principle VII) or a parallel sync index (rejected by Principle VI). The async migration is the simplest correct option.
