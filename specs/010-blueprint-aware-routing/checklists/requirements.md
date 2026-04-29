# Requirements Checklist

**Feature**: [010-blueprint-aware-routing](../spec.md)

This checklist tracks coverage of every functional requirement (FR-XXX) and success criterion (SC-XXX) declared in `spec.md`. Each row pairs a requirement with the specific design artefact (in `plan.md` / `research.md` / `data-model.md` / `contracts/`) that addresses it, and the test (in `tasks.md`) that verifies it.

## Functional requirements

| FR | Summary | Addressed in | Verified by |
|---|---|---|---|
| FR-001 | New `IShellRouteIndex` abstraction in `CShells.AspNetCore.Abstractions` | `contracts/IShellRouteIndex.md`, `plan.md` Project Structure | `DefaultShellRouteIndexTests` |
| FR-002 | `TryMatchAsync(ShellRouteCriteria, ct)` returning `ShellId?` | `contracts/IShellRouteIndex.md`, `data-model.md` `ShellRouteCriteria` | `DefaultShellRouteIndexTests` |
| FR-003 | Index thread-safe | `research.md` R-002, `contracts/IShellRouteIndex.md` Concurrency | `DefaultShellRouteIndexTests` (concurrent-readers test) |
| FR-004 | Singleton DI registration | `plan.md` Project Structure (`ServiceCollectionExtensions.cs`) | `WebRoutingShellResolverLazyActivationTests` (resolves the singleton) |
| FR-005 | Resolver consults index instead of `GetActiveShells` | `contracts/IShellResolverStrategy.md` Migration | `WebRoutingShellResolverLazyActivationTests` |
| FR-006 | Existing match outcomes preserved; cold-blueprint matches added | `spec.md` US1, `contracts/IShellResolverStrategy.md` | `WebRoutingShellResolverLazyActivationTests`, `WebRoutingShellResolverPostReloadTests` |
| FR-007 | Edge-case handling preserved (path with `/`, root-path ambiguity) | `research.md` R-005, `data-model.md` `ShellRouteIndexSnapshot.RootPathAmbiguous` | `DefaultShellRouteIndexTests` (edge-case section) |
| FR-008 | Single structured log entry on no-match | `research.md` R-006, R-007 | `WebRoutingShellResolverDiagnosticsTests` |
| FR-009 | Resolver does NOT call `provider.ListAsync` directly | `contracts/IShellRouteIndex.md`, `plan.md` Constraints | `WebRoutingShellResolverScalingTests` (asserts `ListAsync` only via index) |
| FR-010 | Index refreshes on `ShellAdded`/`ShellRemoved`/`ShellReloaded` | `research.md` R-003, `data-model.md` State Transitions | `DefaultShellRouteIndexTests` (lifecycle invalidation) |
| FR-011 | Index does NOT refresh on `ShellActivated`/`ShellDeactivating` | `research.md` R-003 | `DefaultShellRouteIndexTests` (activation does not invalidate) |
| FR-012 | Provider exception during refresh leaves prior snapshot intact | `research.md` R-003, `contracts/IShellRouteIndex.md` Failure handling | `DefaultShellRouteIndexTests` (refresh-failure test) |
| FR-013 | Concurrent reads see consistent snapshot | `research.md` R-002, `data-model.md` `ShellRouteIndexSnapshot` | `DefaultShellRouteIndexTests` (snapshot-consistency test) |
| FR-014 | `PreWarmShells` continues to work | `plan.md` Summary, `spec.md` US3 | Existing `CShellsStartupHostedServiceTests` (regression-only) |
| FR-015 | `PreWarmShells` with unknown name logs warning, doesn't block | Existing 007 behaviour | Existing `CShellsStartupHostedServiceTests` (regression-only) |
| FR-016 | Host without `PreWarmShells` serves matched routes | `spec.md` US1 | `WebRoutingShellResolverLazyActivationTests`, `ColdStartReloadCycleTests` |
| FR-017 | Misleading "registry remains idle" log line replaced | `research.md` R-006 | `CShellsStartupHostedServiceTests` (log-message regression test) |
| FR-018 | Public surfaces of registry / provider / manager / lifecycle / endpoint handler / middleware unchanged | `plan.md` Summary | Existing 005-009 test suites pass unchanged |
| FR-019 | Custom `IShellResolverStrategy` migration is one-keyword | `contracts/IShellResolverStrategy.md` Migration | Manual review of contract file |
| FR-020 | Shell configuration shape unchanged | `plan.md` Summary, `data-model.md` `ShellRouteEntry` Source | Existing configuration-binding tests pass unchanged |
| FR-021 | `WebRoutingShellResolverOptions` shape preserved (additive only) | `research.md` R-007 | `WebRoutingShellResolverDiagnosticsTests` exercises new options |

## Success criteria

| SC | Summary | Addressed in | Verified by |
|---|---|---|---|
| SC-001 | Cold-start request without pre-warm returns 200 | `spec.md` US1 | `WebRoutingShellResolverLazyActivationTests`, `ColdStartReloadCycleTests` |
| SC-002 | Post-reload request returns 200 (lazy re-activation) | `spec.md` US2 | `WebRoutingShellResolverPostReloadTests`, `ColdStartReloadCycleTests` |
| SC-003 | Startup time delta ≤ ±50 ms vs. baseline (one shell) | `plan.md` Performance Goals | `WebRoutingShellResolverScalingTests` (startup-time assertion) |
| SC-004 | Startup time delta ≤ ±50 ms between 10 vs. 100k blueprints | `research.md` R-001 (no eager scan), `plan.md` Performance Goals | `WebRoutingShellResolverScalingTests` |
| SC-005 | Single log entry per unmatched request | `research.md` R-006 | `WebRoutingShellResolverDiagnosticsTests` |
| SC-006 | All existing 005-009 tests still pass | `plan.md` Constitution Check (V) | CI test suite |
| SC-007 | Workbench sample works without `PreWarmShells` | `plan.md` Project Structure (samples) | `ColdStartReloadCycleTests` (uses Workbench) + manual smoke |
| SC-008 | Downstream `Elsa.ModularServer.Web` works without `PreWarmShells` | `spec.md` Assumptions | Manual smoke check (out of repo) |

## Spec ambiguity resolution

The spec deferred four items to plan/research; this checklist confirms each is now pinned:

| Spec deferred item | Pinned in |
|---|---|
| Final names for `IShellRouteIndex`, `ShellRouteCriteria`, `ShellRouteEntry`, `ShellRouteMatch` | `data-model.md`, `contracts/IShellRouteIndex.md` |
| Duplicate-path policy | `research.md` R-005 (warn-and-first-wins for unique modes; null-on-ambiguous for root-path) |
| Host/header/claim mode strategy | `research.md` R-001 (lazy on first use, snapshot from `ListAsync`) |
| Diagnostic log level | `research.md` R-006 (`Information` for no-match, `Debug` for match, `Warning` for index errors) |
