# Phase 0 Research: Shell Reload Semantics

## Decision: Add provider-level single-shell lookup that returns a nullable shell definition in `CShells.Abstractions`

**Rationale**: Single-shell reload should not have to enumerate the entire provider result set to find one shell. Returning a nullable `ShellSettings` cleanly represents “not found” as a normal lookup outcome while allowing `IShellManager` to enforce strict reload semantics at the management layer.

**Alternatives considered**:

- Reuse `GetShellSettingsAsync()` and filter in memory: rejected because it preserves the current inefficiency and weakens the stated goal of targeted reload.
- Throw from provider lookup when a shell is missing: rejected because a missing shell is not necessarily a provider failure and should remain a regular control-flow outcome.
- Introduce a custom result type: rejected because it adds a new public abstraction without clear value over a nullable return type.

## Decision: Keep single-shell reload strict and keep full reload as the reconciliation command

**Rationale**: Operators need predictable semantics. `ReloadShellAsync(shellId)` should either refresh that shell or fail if the provider no longer defines it. `ReloadAllShellsAsync()` remains the command that reconciles the runtime set to provider state, including removals. Because backward compatibility is not a requirement for this feature, these semantics can replace the current behavior directly rather than being layered in as a compatibility-preserving variant.

**Alternatives considered**:

- Let single-shell reload remove shells missing from the provider: rejected because it makes the targeted API act as an implicit delete command.
- Make full reload update only existing shells: rejected because it leaves cache membership inconsistent with provider state and conflicts with existing operator expectations for a full refresh.

## Decision: Add explicit internal host cache invalidation for affected shell contexts and preserve lazy rebuilds

**Rationale**: `DefaultShellHost` caches `ShellContext` instances and currently has no targeted invalidation path. Reloading settings without evicting cached contexts leaves stale service providers active. An internal invalidation seam lets the manager dispose affected contexts and rely on the host’s existing lazy `GetShell`/`AllShells` rebuild behavior without widening the public `IShellHost` API.

**Alternatives considered**:

- Eagerly rebuild all shell contexts during reload: rejected because it increases reload cost and changes current lazy activation semantics.
- Clear all cached contexts for every reload: rejected because single-shell reload should affect only the targeted shell from an operator perspective.
- Add invalidation to the public `IShellHost` contract: rejected because the constitution prefers keeping internal framework-only seams out of the public API surface.

## Decision: Define “changed shell” for per-shell full-reload notifications as Added, Updated, or Removed

**Rationale**: Full reload emits aggregate notifications plus per-shell notifications for changed shells. That contract needs an explicit definition so tests, payloads, and documentation agree. Restricting changed shells to Added, Updated, or Removed outcomes excludes unchanged shells from redundant notifications.

**Alternatives considered**:

- Notify for every reconciled shell, including unchanged shells: rejected because it creates noise without adding operational value.
- Leave changed-shell semantics implicit: rejected because it makes notification counts and verification ambiguous.

## Decision: Reuse existing lifecycle notifications and add explicit reload notifications with deterministic ordering

**Rationale**: Existing observers may already depend on `ShellActivated`, `ShellDeactivating`, `ShellAdded`, `ShellRemoved`, `ShellUpdated`, and `ShellsReloaded`. The new design adds `ShellReloading` and `ShellReloaded` to provide an outer reload boundary without breaking current event semantics. Ordering is deterministic: start event first, normal lifecycle events next, reload-complete last. Because these are framework-owned public message records emitted through the existing notification pipeline rather than third-party implementation contracts, they remain in `CShells` under the constitution's notification-record exception.

**Alternatives considered**:

- Replace existing lifecycle notifications during reload: rejected because it would break existing notification consumers and reduce observability.
- Emit only aggregate reload notifications for full reload: rejected because the clarified spec requires per-shell reload visibility for changed shells as well.
- Emit only per-shell reload notifications for full reload: rejected because operators also need one clear boundary for the overall reconciliation operation.

## Decision: Cover the feature with both unit and integration tests

**Rationale**: The manager semantics, provider lookup behavior, notification ordering, and host cache invalidation can be unit tested; stale-context correction and reconciliation behavior require integration coverage against the real host/cache interactions.

**Alternatives considered**:

- Unit tests only: rejected because they would not verify stale runtime-context invalidation through the actual host path.
- End-to-end tests only: rejected because they would be slower and make notification ordering failures harder to localize.
