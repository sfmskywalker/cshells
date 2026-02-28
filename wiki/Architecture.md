# Architecture

This page describes the internal design of CShells: how shells are hosted, how features are discovered, how DI containers are built per shell, and how HTTP requests are routed.

---

## Overview

```
┌─────────────────────────────────────────────────────────────────┐
│  Application (ASP.NET Core / Generic Host)                      │
│                                                                 │
│  AddShells() ──► IShellSettingsProvider(s)                      │
│                        │                                        │
│                        ▼                                        │
│                  ShellSettingsCache                             │
│                        │                                        │
│                        ▼                                        │
│                    IShellHost                                   │
│                  (DefaultShellHost)                             │
│            ┌──────────┴──────────┐                             │
│            ▼                     ▼                             │
│       ShellContext           ShellContext                       │
│   (Shell A: IServiceProvider) (Shell B: IServiceProvider)      │
└─────────────────────────────────────────────────────────────────┘
```

---

## Core Concepts

### Shell

A **Shell** is an isolated execution context. It owns:

- A `ShellId` (unique string identifier)
- A list of enabled feature names
- A `ShellSettings` object with configuration data
- A `ShellContext` containing the shell's built `IServiceProvider`

Shells do **not** share service instances. Each shell's container starts from a copy of the root application services and then applies its own feature registrations on top.

### Feature

A **Feature** is a class that implements `IShellFeature` (or a sub-interface). It is the unit of modular functionality. Features:

- Register services via `ConfigureServices(IServiceCollection services)`
- Are discovered automatically by scanning assemblies
- Can declare dependencies on other features
- Can optionally register HTTP endpoints (`IWebShellFeature`), middleware (`IMiddlewareShellFeature`), or post-configuration hooks (`IPostConfigureShellServices`)

### Shell Host

`IShellHost` (implemented by `DefaultShellHost`) manages the lifecycle of all shells:

1. Reads `ShellSettings` from the `ShellSettingsCache`.
2. Discovers features by scanning assemblies for `IShellFeature` implementations.
3. Resolves feature dependencies (topological sort).
4. Builds an `IServiceProvider` per shell.
5. Caches the resulting `ShellContext` instances.

### Shell Settings Cache

`ShellSettingsCache` is the central store for all shell configurations. It is populated at startup by the registered `IShellSettingsProvider` implementations. `IShellManager` writes to it at runtime to add, remove, or update shells.

---

## Class Reference

### `ShellId`

```csharp
public readonly record struct ShellId(string Value);
```

Unique identifier for a shell. Equality is case-sensitive.

### `ShellSettings`

```csharp
public class ShellSettings
{
    public ShellId Id { get; init; }
    public IReadOnlyList<string> EnabledFeatures { get; set; }
    public IDictionary<string, object> ConfigurationData { get; set; }
    public IDictionary<string, Action<IShellFeature>> FeatureConfigurators { get; }
}
```

`ConfigurationData` holds configuration key/value pairs for the shell (e.g., `"WebRouting:Path"` → `""`) which are used to build the shell's `IConfiguration`. `FeatureConfigurators` stores code-first delegates applied to feature instances at build time.

### `ShellContext`

```csharp
public class ShellContext
{
    public ShellSettings Settings { get; }
    public IServiceProvider Services { get; }
}
```

The runtime representation of a shell. Access it via `IShellHost`.

### `ShellFeatureAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ShellFeatureAttribute(string? name = null) : Attribute
{
    public string? Name { get; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public object[] DependsOn { get; set; }   // string or Type elements
    public object[] Metadata { get; set; }
}
```

Optional attribute on feature classes. Without it, the feature name is derived from the class name (e.g., `WeatherFeature` → `"WeatherFeature"`).

### `ShellFeatureContext`

Injectable in feature constructors. Provides the shell settings and all discovered feature descriptors, plus a shared property bag:

```csharp
public class ShellFeatureContext
{
    public ShellSettings Settings { get; }
    public IReadOnlyCollection<ShellFeatureDescriptor> AllFeatures { get; }
    public IDictionary<object, object> Properties { get; }  // shared build-time state
}
```

---

## Feature Discovery

At startup, `DefaultShellHost` scans the provided assemblies (all loaded assemblies by default) for:

- Any non-abstract class implementing `IShellFeature` or a sub-interface

Each discovered type is wrapped in a `ShellFeatureDescriptor` that records:

- The feature name (from `[ShellFeature]` or class name)
- The display name, description
- The list of dependencies (strings and types)
- The implementing type

---

## Feature Dependency Resolution

For each shell, CShells:

1. Takes the shell's list of `EnabledFeatures` names.
2. Looks up the corresponding `ShellFeatureDescriptor` for each.
3. Resolves transitive dependencies.
4. Topologically sorts the full feature set.
5. Instantiates features in that order using `ActivatorUtilities.CreateInstance`.

Features are instantiated with the **root** `IServiceProvider` (not the shell's), so constructors may only inject root-level services plus `ShellSettings` / `ShellFeatureContext`.

---

## DI Container Construction Per Shell

Each shell's `IServiceProvider` is built by:

1. Copying all registrations from the root `IServiceCollection`.
2. Adding `ShellSettings` and `ShellFeatureContext` as singleton registrations.
3. Building a shell-specific `IConfiguration` from `ShellSettings.ConfigurationData`.
4. Calling `ConfigureServices(services)` on each feature in topological order.
5. Calling `PostConfigureServices(services)` on features implementing `IPostConfigureShellServices`.
6. Building the `IServiceProvider`.

Services registered in the root application (e.g., `ILogger<T>`, `IHttpClientFactory`) are available in every shell's container. Shell-specific services (e.g., `IPaymentProcessor`) are only available in the shells that have the corresponding feature enabled.

---

## ASP.NET Core Integration

### Shell Middleware

`MapShells()` inserts `ShellMiddleware` into the pipeline. For each request, the middleware:

1. Runs the registered `IShellResolver` strategies in priority order.
2. Finds the matching `ShellContext`.
3. Creates a scoped `IServiceProvider` from the shell's container.
4. Sets `HttpContext.RequestServices` to the shell-scoped provider.
5. Passes control to the next middleware (endpoint routing).

### Shell Resolver

`IShellResolver` orchestrates multiple `IShellResolverStrategy` implementations, running them in ascending `order` value. The first strategy to return a non-null shell name wins. The built-in strategies:

| Strategy | Order | Matches |
|---|---|---|
| `WebRoutingShellResolver` | 0 | Path prefix, hostname, request header, user claim |
| `DefaultShellResolverStrategy` | 1000 | Always returns `"Default"` (fallback) |

### Dynamic Endpoint Registration

`DynamicShellEndpointDataSource` maintains the list of endpoints across all shells. When shells are added, updated, or removed at runtime, the data source is updated and the ASP.NET Core routing table is refreshed without an application restart.

---

## Notification System

CShells publishes `INotification` records during shell lifecycle events:

| Notification | Trigger |
|---|---|
| `ShellActivated` | Shell DI container built |
| `ShellDeactivating` | Shell about to be shut down |
| `ShellAdded` | Shell added via `IShellManager` |
| `ShellRemoved` | Shell removed via `IShellManager` |
| `ShellUpdated` | Shell updated via `IShellManager` |
| `ShellsReloaded` | All shells reloaded |

Register `INotificationHandler<TNotification>` implementations to react to these events.

---

## Extensibility Points

| Extension Point | Interface | Purpose |
|---|---|---|
| Shell settings source | `IShellSettingsProvider` | Load shell configs from any backend |
| Shell resolution | `IShellResolverStrategy` | Custom per-request shell matching |
| Service exclusions | `IShellServiceExclusionProvider` | Prevent root services from being copied into shells |
| Lifecycle events | `INotificationHandler<T>` | React to shell lifecycle transitions |
| Post-build config | `IPostConfigureShellServices` | Finalize DI registrations after all features run |
| Dependency inference | `IInfersDependenciesFrom<T>` | Automatically inherit another feature's dependencies |
