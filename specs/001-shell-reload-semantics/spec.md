# Feature Specification: Shell Reload Semantics

**Feature Branch**: `001-shell-reload-semantics`  
**Created**: 2026-03-08  
**Status**: Draft  
**Input**: User description: "use strict semantics and also the broader spec that also fixes full-reload stale-context behavior regardless of single shell or all shells. As for notification semantics, do reuse existing notifications BUT also add a dedicated ShellReloaded notification. In fact, add a ShellReloading and ShellReloaded pair of notifications. Finally, do add a provider level GetShellSettingsAsync(ShellId) method - this is indeed cleaner."

## Clarifications

### Session 2026-03-08

- Q: What notification ordering should reload flows guarantee? → A: `ShellReloading` is emitted first, existing lifecycle events run in their normal order as applicable, and `ShellReloaded` is emitted last.
- Q: What should full reload do with shells no longer returned by the provider? → A: Full reload reconciles to provider state by adding newly returned shells, updating changed shells, and removing shells no longer returned.
- Q: What reload notification granularity should full reload use? → A: Full reload emits aggregate reload notifications for the overall operation and also emits per-shell reload notifications for each changed shell.
- Q: What should provider-level single-shell lookup return when a shell is not found? → A: `GetShellSettingsAsync(ShellId)` returns a nullable shell definition when the shell is not found.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reload One Known Shell (Priority: P1)

As an application operator, I want to reload one specific shell by shell ID so that I can apply updated external shell configuration without restarting the application or affecting unrelated shells.

**Why this priority**: Single-shell reload is the new externally visible capability and the direct reason for the feature.

**Independent Test**: Can be fully tested by changing one shell in an external provider, invoking the single-shell reload operation for that shell ID, and confirming that the next use of that shell reflects the new configuration while other shells remain unchanged.

**Acceptance Scenarios**:

1. **Given** a shell exists in the provider and is already active in the application, **When** the operator reloads that shell by shell ID, **Then** the application refreshes that shell from the provider and the next use of that shell reflects the updated configuration.
2. **Given** a shell exists in the provider but is not yet active in the application runtime, **When** the operator reloads that shell by shell ID, **Then** the application makes that shell available with the provider's current configuration.
3. **Given** a shell ID is requested for reload and that shell is not returned by the provider, **When** the reload operation runs, **Then** the operation fails explicitly and does not remove or mutate any existing shell state.

---

### User Story 2 - Reload All Shells Without Stale Runtime State (Priority: P2)

As an application operator, I want a full reload to refresh runtime shell state as well as configuration state so that the application never keeps serving stale shell contexts after a provider refresh.

**Why this priority**: The existing full-reload capability is incomplete if cached runtime state survives after configuration changes.

**Independent Test**: Can be fully tested by activating one or more shells, changing provider-backed configuration, invoking full reload, and confirming that the first subsequent use of each previously active shell reflects the refreshed configuration rather than stale runtime state.

**Acceptance Scenarios**:

1. **Given** one or more shells have already been activated and their provider-backed configuration changes, **When** all shells are reloaded, **Then** the application discards stale runtime shell state so that subsequent requests rebuild from refreshed configuration.
2. **Given** the provider returns newly added shells, changed shells, unchanged shells, and omits previously known shells, **When** all shells are reloaded, **Then** the runtime shell set is reconciled to the provider state by adding new shells, updating changed shells, preserving unchanged shells, and removing shells that are no longer returned.
3. **Given** full reload completes successfully, **When** an operator accesses an unchanged shell, **Then** the shell remains available and behaves consistently with the latest provider data.

---

### User Story 3 - Observe Reload Lifecycle Explicitly (Priority: P3)

As an extension author or operator, I want explicit reload lifecycle notifications so that I can distinguish reload operations from add, update, and remove operations while keeping existing lifecycle notifications intact.

**Why this priority**: Observability and extension behavior depend on knowing when a reload starts and when it finishes successfully.

**Independent Test**: Can be fully tested by registering lifecycle observers, performing single-shell and full-shell reload operations, and verifying that reload start and completion notifications are emitted in the expected order together with existing lifecycle events.

**Acceptance Scenarios**:

1. **Given** an observer listens for shell lifecycle notifications, **When** a single-shell reload starts, **Then** the observer receives a reload-start notification that identifies the target shell.
2. **Given** an observer listens for shell lifecycle notifications, **When** a single-shell reload or full reload completes successfully, **Then** the observer receives a reload-complete notification that describes the completed reload scope and is emitted after all other lifecycle notifications for that reload have already been published.
3. **Given** an observer listens for shell lifecycle notifications, **When** a full reload processes changed shells, **Then** the observer receives one aggregate reload-start notification for the whole operation, per-shell reload notifications for each changed shell, and one aggregate reload-complete notification after the reconciliation finishes.

### Edge Cases

- What happens when a single-shell reload is requested for a shell ID that does not exist in the provider but a stale runtime context is still cached? The operation must fail explicitly and must not evict the current runtime state as part of that failure.
- How does the system handle a full reload when some shells were previously activated and others were never activated? Previously activated shells must discard stale runtime state; shells never activated remain lazily available without requiring eager activation.
- What happens when observers are registered for both existing lifecycle events and the new reload events? They must receive a predictable, non-contradictory event sequence where aggregate `ShellReloading` is published first for a full reload, existing lifecycle notifications occur in their normal order as applicable, per-shell reload notifications are emitted for changed shells, and the aggregate `ShellReloaded` is published last.
- What happens when the provider-level single-shell lookup cannot find the requested shell? The lookup returns no shell definition, allowing the management API to fail strict single-shell reload without treating provider lookup absence as an exceptional provider failure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a runtime management operation that reloads exactly one shell identified by shell ID.
- **FR-002**: The single-shell reload operation MUST use strict semantics: if the provider does not return the requested shell, the operation MUST fail explicitly and MUST NOT remove, replace, or otherwise mutate the currently loaded shell state for that shell ID.
- **FR-003**: The single-shell reload operation MUST refresh the requested shell from the provider's latest available data and make that refreshed state effective for the next use of that shell.
- **FR-004**: The shell settings provider contract MUST support retrieving one shell by shell ID without requiring callers to enumerate all shells.
- **FR-004a**: Provider-level single-shell lookup MUST return a nullable shell definition when the shell is not found, rather than signaling that outcome as an exception.
- **FR-005**: The full reload operation MUST refresh provider-backed shell configuration and MUST also invalidate any previously cached runtime shell state that could otherwise continue serving stale shell behavior.
- **FR-006**: The stale-runtime-state fix MUST apply consistently whether shells are reloaded individually or all shells are reloaded together.
- **FR-006a**: Full reload MUST reconcile the complete runtime shell set to provider state by adding newly returned shells, updating changed shells, preserving unchanged shells, and removing shells no longer returned by the provider.
- **FR-007**: The system MUST preserve existing add, update, remove, activation, and deactivation lifecycle notifications during reload operations wherever those lifecycle transitions still occur.
- **FR-008**: The system MUST add a dedicated `ShellReloading` notification that is emitted when a reload operation begins.
- **FR-009**: The system MUST add a dedicated `ShellReloaded` notification that is emitted only after a reload operation completes successfully.
- **FR-010**: Dedicated reload notifications MUST distinguish between single-shell reload scope and full-reload scope.
- **FR-011**: A failed reload operation MUST NOT emit a successful reload-complete notification.
- **FR-012**: Single-shell reload MUST affect only the targeted shell from an operator perspective and MUST NOT refresh unrelated shells as visible side effects.
- **FR-013**: Reload behavior and notification behavior MUST be documented in the runtime shell management guidance and multi-provider guidance.
- **FR-014**: Reload notification ordering MUST be deterministic: `ShellReloading` is emitted first, existing lifecycle notifications are emitted in their normal order as applicable to the reload, and `ShellReloaded` is emitted last.
- **FR-015**: Full reload MUST emit aggregate reload notifications for the overall reconciliation operation and MUST also emit per-shell reload notifications for each shell whose definition changes during that reconciliation.

### Key Entities *(include if feature involves data)*

- **Shell Definition**: The externally sourced description of one shell, identified by shell ID and containing the enabled features and configuration values used to build runtime shell state.
- **Runtime Shell Context**: The active in-memory representation of a shell that serves requests or resolves services and can become stale if it outlives newer provider data.
- **Reload Operation**: A management action that refreshes either one shell or all shells from provider data while coordinating runtime state replacement and lifecycle notifications.
- **Reload Notification**: A lifecycle event that marks the start or successful completion of a reload operation and identifies whether the scope is a single shell or all shells.
- **Provider Lookup Result**: The outcome of asking a provider for one shell by shell ID, including whether a current definition exists for that shell.
- **Provider Lookup Result**: The nullable outcome of asking a provider for one shell by shell ID, where the absence of a shell definition means the shell is not currently provided.

## Assumptions

- Single-shell reload is intentionally strict and is not a reconciliation command; it does not remove a shell merely because the provider no longer returns it.
- Full reload remains the reconciliation command for refreshing the complete provider-backed shell set while correcting stale runtime state for any previously activated shells.
- Full reload reconciliation applies to both shell configuration membership and cached runtime shell contexts.
- Existing lifecycle notifications remain part of the observable behavior and the new reload notifications supplement them rather than replace them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a successful single-shell reload, 100% of subsequent accesses to that shell reflect the provider's latest configuration with no stale runtime state observed after completion.
- **SC-002**: When a single-shell reload targets a shell ID absent from the provider, 100% of attempts fail explicitly and leave the prior runtime state unchanged.
- **SC-003**: After a successful full reload, 100% of previously activated shells rebuild from refreshed provider data on their next use rather than continuing to serve stale runtime state.
- **SC-004**: Observers can distinguish reload start and reload completion for both single-shell and full-reload operations in 100% of tested lifecycle sequences.
