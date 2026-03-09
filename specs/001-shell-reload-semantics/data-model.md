# Data Model: Shell Reload Semantics

## Shell Definition

Represents the provider-sourced description of a shell that can be loaded into the runtime.

### Fields

- `ShellId`: stable shell identifier
- `EnabledFeatures`: ordered list of explicitly enabled features
- `ConfigurationData`: shell-scoped configuration values
- `FeatureConfigurators`: optional code-first runtime configurators

### Validation Rules

- `ShellId` must be non-empty and valid per existing `ShellId` rules.
- `EnabledFeatures` must reference known discoverable features when the shell context is built.
- Missing provider result is represented by `null`, not by an exception.

## Runtime Shell Context

Represents the in-memory, built shell state held by `DefaultShellHost`.

### Fields

- `Id`: shell identifier
- `Settings`: current shell definition used to build the context
- `ServiceProvider`: shell-specific service provider
- `EnabledFeatures`: dependency-resolved feature list used by the context

### State Transitions

- `NotBuilt` → `Active`: shell is first accessed and built from settings
- `Active` → `Invalidated`: reload or removal marks the cached context stale and disposes it
- `Invalidated` → `Active`: next access rebuilds the shell from latest settings
- `Active` → `Removed`: full reconciliation or explicit removal deletes the shell from cache membership and runtime cache

## Reload Operation

Represents one runtime management refresh action.

### Fields

- `Scope`: `SingleShell` or `AllShells`
- `TargetShellId`: nullable; populated only for single-shell operations and per-shell notifications during a full reload
- `Strict`: `true` for single-shell reload semantics
- `ChangedShellIds`: collection of affected shell IDs for aggregate full-reload completion

### Validation Rules

- `SingleShell` scope requires a non-null `TargetShellId`.
- `AllShells` scope must not imply eager activation of shells that were not previously built.
- Single-shell reload must fail if provider lookup for `TargetShellId` returns `null`.

## Reload Notification

Represents explicit reload lifecycle observability.

### Fields

- `Phase`: `Reloading` or `Reloaded`
- `Scope`: `SingleShell` or `AllShells`
- `TargetShellId`: nullable for aggregate full-reload notifications, populated for single-shell and per-shell changed notifications
- `ChangedShellIds`: optional collection for aggregate full-reload completion, containing only Added, Updated, or Removed shells

### Relationships

- A reload notification wraps an outer operation boundary around existing lifecycle notifications.
- Aggregate full-reload notifications describe the overall reconciliation operation.
- Per-shell reload notifications describe each changed shell processed inside that reconciliation.
- Changed shells are limited to reconciliation outcomes of Added, Updated, or Removed.

## Provider Lookup Result

Represents the outcome of retrieving one shell definition from a provider.

### Fields

- `RequestedShellId`: the shell identifier requested by the manager
- `Settings`: nullable shell definition returned by the provider

### Validation Rules

- `Settings == null` means the shell is not currently defined by that provider surface.
- A null result is not itself an error condition; the manager decides how to interpret it based on reload scope.
