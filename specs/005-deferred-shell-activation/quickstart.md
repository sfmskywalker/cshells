# Quickstart: Implement Deferred Shell Activation and Atomic Shell Reconciliation

## Purpose

Use this guide when implementing feature `005-deferred-shell-activation` so CShells records desired shell definitions immediately, preserves last-known-good applied runtimes, refreshes the feature catalog safely, and exposes only committed applied runtimes to routing and endpoints.

## 1. Confirm the current single-truth seams

Inspect the existing runtime pieces that currently blend configured shell data and active runtime availability.

Primary inspection targets:

```bash
cd /Users/sipke/Projects/ValenceWorks/cshells/main

grep -n "DefaultShell\|GetShell\|AllShells\|EvictShell" src/CShells/Hosting/DefaultShellHost.cs

grep -n "ReloadShellAsync\|ReloadAllShellsAsync\|AddShellAsync\|UpdateShellAsync\|RemoveShellAsync" src/CShells/Management/DefaultShellManager.cs

grep -n "GetAll\|GetById\|Load" src/CShells/Configuration/IShellSettingsCache.cs src/CShells/Configuration/ShellSettingsCache.cs

grep -n "Resolve" src/CShells/Resolution/DefaultShellResolverStrategy.cs src/CShells.AspNetCore/Resolution/WebRoutingShellResolver.cs

grep -n "ShellAdded\|ShellRemoved\|ShellReloaded\|ShellsReloaded" src/CShells.AspNetCore/Notifications/ShellEndpointRegistrationHandler.cs
```

## 2. Introduce a dual-state runtime model

Add a runtime-state model that keeps configured desired state separate from applied runtime state.

- Preserve `ShellSettings` as the authoritative desired definition input.
- Add per-shell runtime records that store desired generation, applied generation, reconciliation outcome, and blocking reason.
- Ensure a shell can remain configured with no applied runtime.
- Add a small public inspection surface in `CShells.Abstractions` for operator-visible desired-vs-applied status.

## 3. Add a refreshable runtime feature catalog

Replace one-time feature discovery assumptions with a refreshable snapshot model.

- Re-evaluate all configured `IFeatureAssemblyProvider` sources before each reconciliation pass.
- Build a candidate catalog snapshot off to the side.
- Fail the refresh explicitly on duplicate feature IDs or catalog inconsistencies.
- Keep the previously committed catalog untouched on refresh failure.

## 4. Implement candidate-build then atomic commit semantics per shell

Reconciliation should build successor runtimes before mutating applied runtime state.

- Validate the latest desired generation against the committed catalog snapshot.
- Record `DeferredDueToMissingFeatures` with missing-feature details when the catalog does not satisfy the desired definition.
- Record `Failed` with an actionable failure reason when candidate build or validation fails for other reasons.
- Preserve the last-known-good applied runtime until a ready candidate can be committed.
- Publish deactivation/activation lifecycle events only around real applied-runtime commits.

## 5. Unify startup, single-shell reload, and full reconciliation semantics

Move startup and runtime management flows onto one reconciliation model.

- Startup records desired state for every configured shell and applies only satisfiable shells.
- `ReloadShellAsync(shellId)` refreshes the catalog first and reconciles the targeted shell with the same candidate-build semantics.
- `ReloadAllShellsAsync()` refreshes the catalog first and re-evaluates every configured shell independently.
- Removal remains the only path that intentionally deletes a shell from desired state.

## 6. Align routing, endpoints, and fallback resolution to applied state only

Ensure web runtime behavior never exposes desired-only shells.

- `WebRoutingShellResolver` should resolve only shells with an applied active runtime.
- `DefaultShellResolverStrategy` must preserve explicit `Default` semantics: if `Default` is configured but unapplied, fallback must report it unavailable.
- `ShellEndpointRegistrationHandler` and `DynamicShellEndpointDataSource` should register endpoints only for committed applied runtimes.
- Desired-only deferred or failed shells must contribute no endpoints.

## 7. Add operator-visible reconciliation status and clear notifications

Make drift and blocking reasons observable without confusing desired-state updates with activations.

- Expose desired generation, applied generation, reconciliation outcome, sync status, and blocking/drift reason for each configured shell.
- Keep desired-state notifications separate from applied-runtime lifecycle notifications.
- Ensure aggregate reload notifications provide enough information for endpoint registration and operator inspection to distinguish active, deferred, and failed shells.

## 8. Validate the mixed-state scenarios

Run focused tests for the revised architecture, then the broader suites.

```bash
cd /Users/sipke/Projects/ValenceWorks/cshells/main

dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~DeferredShellActivation|FullyQualifiedName~ShellManager|FullyQualifiedName~DefaultShellHost|FullyQualifiedName~ShellResolver"

dotnet test tests/CShells.Tests.EndToEnd/

dotnet test tests/CShells.Tests/
```

## Expected Outcomes

- Desired shell definitions remain authoritative even when not currently buildable.
- The runtime preserves last-known-good applied shells during deferred or failed updates.
- Feature catalog refreshes can make previously deferred shells satisfiable without restart.
- Duplicate feature IDs fail the refresh before any applied state changes.
- Only committed applied runtimes are routable and endpoint-visible.
- Explicit `Default` fallback behavior is strict and never silently substitutes another shell.

