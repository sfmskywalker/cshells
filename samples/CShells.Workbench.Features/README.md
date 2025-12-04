# CShells.Workbench.Features

This project demonstrates the **recommended approach** for organizing CShells features in a separate class library.

## Key Points

### Minimal Dependencies

This project references **only** `CShells.AspNetCore.Abstractions`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\CShells.AspNetCore.Abstractions\CShells.AspNetCore.Abstractions.csproj"/>
</ItemGroup>
```

**No dependencies on:**
- `CShells` (core framework implementation)
- `CShells.AspNetCore` (middleware, routing, etc.)
- Any other implementation packages

This keeps your feature library lightweight and focused on defining feature contracts and behavior.

### What's Included

This library contains the `CoreFeature` that provides shared services used by all shells:

- **ITenantInfo** - Tenant information interface
- **IAuditLogger** - Audit logging interface
- **ITimeService** - Time service interface
- **CoreFeature** - The feature implementation that registers these services and exposes the root endpoint

## Benefits of This Approach

### 1. **Separation of Concerns**
Feature definitions are separated from the application host. Features don't need to know about:
- How shells are configured
- How routing works
- How the application is hosted

### 2. **Reusability**
Features defined in this library can be:
- Shared across multiple applications
- Packaged as NuGet packages
- Versioned independently

### 3. **Minimal Dependencies**
Your feature library only depends on abstractions, which means:
- Faster compile times
- Smaller dependency graph
- Easier to test in isolation
- No breaking changes from framework implementation updates

### 4. **Clear Architecture**
The project structure makes it obvious:
```
YourSolution/
├── YourApp/                  # References full CShells packages
│   └── YourApp.csproj
└── YourApp.Features/         # References only abstractions
    └── YourApp.Features.csproj
```

## Creating Your Own Feature Library

To create a similar structure in your application:

### 1. Create the Feature Library

```bash
dotnet new classlib -n YourApp.Features
cd YourApp.Features
dotnet add reference ../path/to/CShells.AspNetCore.Abstractions
```

### 2. Define Your Features

```csharp
using CShells.AspNetCore.Features;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace YourApp.Features;

[ShellFeature("MyFeature", DisplayName = "My Feature")]
public class MyFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMyService, MyService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet("my-endpoint", () => "Hello from my feature!");
    }
}
```

### 3. Reference from Your Main Application

```xml
<ItemGroup>
  <ProjectReference Include="..\YourApp.Features\YourApp.Features.csproj" />
  <ProjectReference Include="CShells" />
  <ProjectReference Include="CShells.AspNetCore" />
</ItemGroup>
```

### 4. Register in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// CShells automatically scans all loaded assemblies for features
builder.AddShells();

var app = builder.Build();
app.MapShells();
app.Run();
```

## See Also

- [Main Workbench README](../CShells.Workbench/README.md) - Full sample application documentation
- [Root README](../../README.md) - CShells package overview and quick start
