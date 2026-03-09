# Quickstart: Validate Shell Reload Semantics

## Prerequisites

- The feature branch `001-shell-reload-semantics` is checked out.
- A shell settings provider is available in the test environment.
- Existing runtime shell management tests build and pass before changes begin.

## 1. Implement targeted provider lookup

- Extend `IShellSettingsProvider` in `CShells.Abstractions` with `GetShellSettingsAsync(ShellId, CancellationToken)` returning `ShellSettings?`.
- Update built-in provider implementations so single-shell lookup does not require full enumeration in the manager.
- Add unit tests for found and not-found lookup behavior.

## 2. Implement strict single-shell reload

- Extend `IShellManager` in `CShells.Abstractions` with `ReloadShellAsync(ShellId, CancellationToken)`.
- In `DefaultShellManager`, use provider single-shell lookup for the targeted reload path.
- Ensure missing-provider results fail explicitly without deleting or mutating the currently loaded shell state.

## 3. Fix stale runtime shell contexts

- Add an internal host-level shell-context invalidation seam for affected shells.
- Dispose invalidated shell contexts and preserve lazy rebuild behavior.
- Ensure both single-shell reload and full reload invalidate the right cached runtime contexts.

## 4. Add reload notifications

- Add `ShellReloading` and `ShellReloaded` notification types.
- Emit notifications in deterministic order around existing lifecycle events.
- For full reload, emit aggregate start/end notifications plus per-shell reload notifications for changed shells.

## 5. Update documentation

- Update runtime shell management guidance to document strict single-shell reload.
- Update multi-provider guidance to document targeted provider lookup, full-reload reconciliation semantics, and the notification behavior providers should expect during reload operations.

## 6. Validate behavior

- Run focused unit tests for provider lookup, manager reload behavior, and notification ordering.
- Run integration tests for stale-context invalidation and full-reload reconciliation.
- Run the full test suite:

```bash
dotnet test
```

## Expected Outcomes

- Single-shell reload refreshes only the targeted shell.
- Missing-provider single-shell reload fails explicitly and leaves runtime state unchanged.
- Full reload reconciles shell membership and prevents stale `ShellContext` reuse.
- Observers can distinguish reload boundaries from existing lifecycle events.