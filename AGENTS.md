# AGENTS.md — CShells

## Architecture

CShells is a multi-package library. Package separation is intentional and enforced:

| Project type | Reference |
|---|---|
| Feature class library | `CShells.Abstractions` or `CShells.AspNetCore.Abstractions` |
| Main ASP.NET Core app | `CShells` + `CShells.AspNetCore` |

**Data flow at startup:**
`AddShells()`/`AddCShellsAspNetCore()` → `IShellSettingsProvider` loads `ShellSettings` into `ShellSettingsCache` → `DefaultShellHost` (lazy) builds a per-shell `IServiceProvider` from root services + feature `ConfigureServices` calls → `ShellMiddleware` resolves the shell per request via `IShellResolverStrategy` and swaps `HttpContext.RequestServices` to the shell's scoped provider.

Root service descriptors are **bulk-copied** into each shell's `IServiceCollection`. CShells infrastructure types (`IShellHost`, `IShellContextScopeFactory`, `IRootServiceCollectionAccessor`, `IShellManager`) are excluded from this copy — see `DefaultShellServiceExclusionProvider`.

## Feature System

Features are discovered by scanning assemblies for types implementing `IShellFeature`. The feature name is the `[ShellFeature("Name")]` attribute value, or the class name with the `Feature`/`ShellFeature` suffix stripped.

```csharp
// Services only (no web endpoints)
[ShellFeature("Analytics", DependsOn = ["Posts"])]
public class AnalyticsFeature : IShellFeature, IConfigurableFeature<AnalyticsOptions>
{
    public void Configure(AnalyticsOptions options) => _options = options; // called before ConfigureServices
    public void ConfigureServices(IServiceCollection services) { ... }
}

// Services + ASP.NET Core endpoints
[ShellFeature("Core")]
public class CoreFeature(ShellSettings shellSettings) : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services) { ... }
    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) { ... }
}
```

**Critical constraint:** Feature constructors may only inject root-level services (logging, configuration) and optionally `ShellSettings` or `ShellFeatureContext`. They **cannot** inject services registered by other features — those are only available after `ConfigureServices` runs.

**Extension interfaces** (all in `src/CShells.Abstractions/Features/`):
- `IConfigurableFeature<TOptions>` — auto-binds options from `IConfiguration` before `ConfigureServices`
- `IPostConfigureShellServices` — runs once after all features complete `ConfigureServices`, before the shell `IServiceProvider` is built; use for finalization patterns (e.g., wrapping `AddMassTransit`)
- `IInfersDependenciesFrom<TBaseFeature>` — inherits the base feature's dependency graph
- `DependsOn` in `[ShellFeature]` accepts `string` names or `typeof(SomeFeature)` values; resolved topologically

## Shell Configuration

**appsettings.json** (source of truth for the Workbench sample):
```json
"CShells": {
  "Shells": [
    {
      "Name": "Contoso",
      "Features": [
        "Core",
        "Posts",
        { "Name": "Analytics", "TopPostsCount": 10 }
      ],
      "Configuration": { "WebRouting": { "Path": "contoso" }, "Plan": "Enterprise" }
    }
  ]
}
```

**Code-first** (overrides config after binding, before `ConfigureServices`):
```csharp
builder.AddCShells(cshells => cshells
    .AddShell("MyShell", shell => shell
        .WithFeature<CoreFeature>()
        .WithFeature<AnalyticsFeature>(f => f.TopPostsCount = 5)));
```

`ShellSettings.ConfigurationData` uses colon-separated keys (e.g., `"WebRouting:Path"`) for hierarchical IConfiguration access inside the shell.

## Shell Resolution Pipeline

Strategies implement `IShellResolverStrategy` and are ordered by `[ResolverOrder(N)]` (lower runs first). Built-ins:
- `WebRoutingShellResolver` (order 0) — resolves by URL path, HTTP host, header, or user claim
- `DefaultShellResolverStrategy` (order 1000) — fallback, always returns shell ID `"Default"`

Register custom strategies with `.ConfigureResolverPipeline(pipeline => pipeline.Use<MyStrategy>())`.

## Runtime Shell Management

`IShellManager` supports hot add/remove/update/reload without app restart. `DynamicShellEndpointDataSource` signals ASP.NET Core routing to re-enumerate endpoints via `IChangeToken`. Lifecycle events are published as notifications (`ShellActivated`, `ShellDeactivating`, `ShellAdded`, `ShellRemoved`, `ShellReloaded`, etc.) — subscribe by implementing `INotificationHandler<T>`.

## Background Workers

Use `IShellContextScopeFactory` to work within a shell's DI scope outside an HTTP request:
```csharp
foreach (var shell in shellHost.AllShells)
{
    using var scope = scopeFactory.CreateScope(shell);
    var svc = scope.ServiceProvider.GetRequiredService<IMyService>();
}
```
See `samples/CShells.Workbench/Background/ShellDemoWorker.cs` for a working example.

## Developer Workflows

```bash
dotnet build                          # build solution
dotnet test                           # all tests
dotnet test tests/CShells.Tests/      # unit + integration only
cd samples/CShells.Workbench && dotnet run  # sample app
```

Package versions are centrally managed in `Directory.Packages.props` — never set `Version` on a `<PackageReference>`.

## Testing Patterns

- Unit tests → `tests/CShells.Tests/Unit/`
- Integration tests → `tests/CShells.Tests/Integration/`; use `DefaultShellHostFixture` (in `TestHelpers/`) to construct `DefaultShellHost` instances
- E2E tests → `tests/CShells.Tests.EndToEnd/` via `WebApplicationFactory<Program>`
- `TestFixtures.CreateRootServices()` produces a minimal root `IServiceCollection`/`IServiceProvider` for test isolation
- Test file names mirror the class under test with a `Tests` suffix

## C# Conventions

- C# 14; file-scoped namespaces; `var` always; expression-bodied single-line members; primary constructors preferred
- Private fields: camelCase, **no underscore** prefix (e.g., `_options` → `options` when a primary ctor parameter, `_field` only for non-primary-ctor fields)
- Guard clauses via `Guard.Against.Null(...)` (defined in `src/CShells.Abstractions/Guard.cs`)
- Collection expressions (`[..list]`) over `new List<T>` wherever possible
- `[ResolverOrder(N)]` attribute controls strategy ordering — lower wins

## Key Reference Files

| Purpose | Path |
|---|---|
| Feature interfaces | `src/CShells.Abstractions/Features/` |
| Shell host (core orchestrator) | `src/CShells/Hosting/DefaultShellHost.cs` |
| Feature discovery (reflection) | `src/CShells/Features/FeatureDiscovery.cs` |
| ASP.NET Core wiring | `src/CShells.AspNetCore/Extensions/ApplicationBuilderExtensions.cs` |
| Reference feature implementations | `samples/CShells.Workbench.Features/` |
| Integration test helper | `tests/CShells.Tests/TestHelpers/DefaultShellHostFixture.cs` |

