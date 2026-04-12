# Getting Started

This page walks you through installing CShells, creating your first feature, configuring a shell, and running the application.

---

## Prerequisites

- .NET 10 or later
- An ASP.NET Core web application (for web features) or a .NET generic host application

---

## 1. Install Packages

```bash
dotnet add package CShells
dotnet add package CShells.AspNetCore
```

For loading shell configurations from external JSON files, also add:

```bash
dotnet add package CShells.Providers.FluentStorage
dotnet add package FluentStorage
```

### Recommended Project Layout

Keep your feature definitions in a separate class library that takes only a lightweight dependency:

```
YourSolution/
├── src/
│   ├── YourApp/                           # Main ASP.NET Core application
│   │   └── YourApp.csproj                 # Refs: CShells, CShells.AspNetCore, YourApp.Features
│   └── YourApp.Features/                  # Feature definitions
│       └── YourApp.Features.csproj        # Refs: CShells.AspNetCore.Abstractions only
```

This keeps your feature library lightweight and independent of the CShells runtime.

---

## 2. Create a Feature

### Service-only feature

```csharp
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

public class CoreFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITimeService, TimeService>();
    }
}
```

### Web feature with HTTP endpoints

```csharp
using CShells.AspNetCore.Features;
using Microsoft.Extensions.DependencyInjection;

[ShellFeature("Weather", DisplayName = "Weather API", DependsOn = [typeof(CoreFeature)])]
public class WeatherFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWeatherService, WeatherService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet("weather", (IWeatherService svc) => svc.GetForecast());
    }
}
```

The `[ShellFeature]` attribute is **optional**. Use it only when you need to set an explicit name, display name, or dependencies. Without the attribute, the feature name is derived from the class name (e.g., `WeatherFeature` → `"WeatherFeature"`).

---

## 3. Configure Shells

In `appsettings.json`:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": ["CoreFeature", "Weather"],
        "Configuration": {
          "WebRouting": {
            "Path": ""
          }
        }
      }
    ]
  }
}
```

---

## 4. Register CShells in `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register CShells (reads from appsettings.json, section "CShells")
builder.AddShells();

var app = builder.Build();

// Register shell middleware and endpoints
app.MapShells();

app.Run();
```

`AddShells()` preserves the default host-derived feature assembly discovery behavior. To switch to explicit feature assembly selection:

```csharp
builder.AddShells(cshells =>
{
    cshells.WithConfigurationProvider(builder.Configuration);
    cshells.FromAssemblies(typeof(CoreFeature).Assembly);
});
```

You can append `FromHostAssemblies()` to opt the built-in host-derived assembly set back into explicit mode, or `WithAssemblyProvider(...)` to contribute assemblies from a custom `IFeatureAssemblyProvider` implementation.

---

## 5. Run and Test

Start the application and navigate to the endpoints registered by your shell features. For the example above, `GET /weather` is served by the Default shell.

---

## Next Steps

- [Creating Features](Creating-Features) — more about feature types and options
- [Configuring Shells](Configuring-Shells) — code-first, FluentStorage, multiple providers
- [Shell Resolution](Shell-Resolution) — how shells are selected per request
