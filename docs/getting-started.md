# Getting Started with CShells

CShells is a lightweight shell and feature system for .NET that enables modular, multi-tenant applications with per-shell DI containers and config-driven features.

## Installation

Add the relevant NuGet packages to your project:

```xml
<!-- Core library -->
<PackageReference Include="CShells" />

<!-- ASP.NET Core integration -->
<PackageReference Include="CShells.AspNetCore" />

<!-- Optional: load shells from blob/file storage -->
<PackageReference Include="CShells.Providers.FluentStorage" />
```

For feature libraries that only need the abstractions:

```xml
<PackageReference Include="CShells.AspNetCore.Abstractions" />
```

## Quick Start

### 1. Define Features

Create a class library for your features. Each feature implements `IShellFeature` (services only) or `IWebShellFeature` (services + endpoints).

```csharp
using CShells.AspNetCore.Features;
using CShells.Features;

// Service-only feature
[ShellFeature("Cache", DisplayName = "Cache Services")]
public class CacheFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
    }
}

// Web feature with endpoints
[ShellFeature("Blog", DependsOn = ["Cache"], DisplayName = "Blog Posts")]
public class BlogFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPostRepository, InMemoryPostRepository>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet("/posts", (IPostRepository repo) => repo.GetAll());
        endpoints.MapGet("/posts/{id:int}", (int id, IPostRepository repo) => repo.GetById(id));
    }
}
```

### 2. Configure Shells

Define your shells in `appsettings.json`:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": ["Cache", "Blog"],
        "Configuration": {
          "WebRouting": { "Path": "" }
        }
      },
      {
        "Name": "Premium",
        "Features": ["Cache", "Blog", "Analytics"],
        "Configuration": {
          "WebRouting": { "Path": "premium" }
        }
      }
    ]
  }
}
```

### 3. Register CShells in Program.cs

```csharp
using CShells.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register CShells from configuration
builder.AddShells();

var app = builder.Build();

app.UseRouting();
app.MapShells();
app.Run();
```

CShells will:

1. Scan the host-derived default feature assembly set for all discovered shell features
2. Load shell settings from the `CShells` configuration section
3. Build isolated DI containers per shell, registering only the features each shell enables
4. Map shell-scoped endpoints into ASP.NET Core's routing system

To switch to explicit feature assembly selection, configure it on the fluent builder:

Use `From*` members to select feature-discovery sources, and `WithAssemblyProvider(...)` when attaching a provider that contributes assemblies.

```csharp
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.FromAssemblies(typeof(BlogFeature).Assembly);
    cshells.FromHostAssemblies();
});
```

Any call to `FromAssemblies(...)`, `FromHostAssemblies()`, or `WithAssemblyProvider(...)` switches CShells into explicit provider mode. In that mode, only the assemblies returned by those appended providers are scanned, and duplicate assemblies are discovered once in first-seen order.

### Testing the Result

```
GET /                → Default shell  (Cache + Blog)
GET /posts           → Default shell posts
GET /premium/        → Premium shell  (Cache + Blog + Analytics)
GET /premium/posts   → Premium shell posts
```

## Project Layout

A typical CShells application looks like this:

```
MyApp/
├── MyApp.Features/              ← Feature class library (references CShells.AspNetCore.Abstractions)
│   ├── Cache/CacheFeature.cs
│   ├── Blog/BlogFeature.cs
│   └── Analytics/AnalyticsFeature.cs
├── MyApp/                       ← ASP.NET Core web app (references CShells.AspNetCore)
│   ├── Program.cs
│   └── appsettings.json
```

## Key Concepts

### Shells

A **shell** is an isolated execution context with its own DI container. Think of it as a tenant, an environment, or a configuration variant. Each shell enables a subset of the available features.

### Features

A **feature** is a modular unit of functionality:

- `IShellFeature` — registers services only
- `IWebShellFeature` — registers services and maps HTTP endpoints
- `[ShellFeature]` — attribute that names the feature, sets display metadata, and declares dependencies
- Features are discovered by assembly scanning at startup

### Shell Resolution

CShells resolves which shell handles a request using **resolvers**. The default resolver (`WebRoutingShellResolver`) supports:

- **Path-based routing** — `WebRouting:Path` in shell configuration
- **Host-based routing** — `WebRouting:Host` in shell configuration
- **Header-based routing** — `options.HeaderName = "X-Tenant-Id"`
- **Claim-based routing** — `options.ClaimKey = "tenant_id"`

See [Shell Resolution](./shell-resolution.md) for full details.

## Next Steps

- [Feature Configuration](./feature-configuration.md) — per-shell feature settings
- [Shell Lifecycle](./shell-lifecycle.md) — per-shell startup and shutdown hooks
- [Shell Resolution](./shell-resolution.md) — customize request-to-shell mapping
- [Multiple Shell Providers](./multiple-shell-providers.md) — load shells from multiple sources
- [Integration Patterns](./integration-patterns.md) — integrate CShells into existing apps
