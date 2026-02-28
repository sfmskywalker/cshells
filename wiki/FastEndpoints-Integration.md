# FastEndpoints Integration

CShells integrates with [FastEndpoints](https://fast-endpoints.com/) via the `CShells.FastEndpoints` package, providing per-shell endpoint isolation and automatic endpoint discovery.

---

## Installation

```bash
dotnet add package CShells.FastEndpoints
```

In your feature class libraries, reference only the abstractions package:

```bash
dotnet add package CShells.FastEndpoints.Abstractions
```

---

## Creating a FastEndpoints Feature

Implement `IFastEndpointsShellFeature` in your feature class:

```csharp
using CShells.FastEndpoints.Features;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

[ShellFeature("Products", DependsOn = ["Core", "FastEndpoints"])]
public class ProductsFeature : IFastEndpointsShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IProductRepository, ProductRepository>();
    }
}
```

FastEndpoints endpoints in the same assembly are automatically discovered and registered for shells that have this feature enabled.

---

## Creating an Endpoint

```csharp
using FastEndpoints;

public class GetProductsEndpoint : EndpointWithoutRequest<List<ProductDto>>
{
    private readonly IProductRepository _repository;

    public GetProductsEndpoint(IProductRepository repository)
    {
        _repository = repository;
    }

    public override void Configure()
    {
        Get("products");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var products = await _repository.GetAllAsync(ct);
        await SendAsync(products.Select(p => new ProductDto(p.Id, p.Name)).ToList(), cancellation: ct);
    }
}
```

---

## Shell Configuration

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Default",
        "Features": ["Core", "FastEndpoints", "Products"],
        "Configuration": {
          "WebRouting": {
            "Path": "",
            "RoutePrefix": "api/v1"
          },
          "FastEndpoints": {
            "EndpointRoutePrefix": "fe"
          }
        }
      }
    ]
  }
}
```

With this configuration:
- Shell path: (root)
- `WebRouting:RoutePrefix`: `api/v1` — applied to all endpoints
- `FastEndpoints:EndpointRoutePrefix`: `fe` — applied only to FastEndpoints

The `GET products` endpoint is accessible at `/api/v1/fe/products`.

---

## Route Prefix Hierarchy

| Configuration Key | Scope | Description |
|---|---|---|
| `WebRouting:Path` | Shell | URL path that routes requests to this shell |
| `WebRouting:RoutePrefix` | All endpoints | Applied to all endpoints (minimal APIs, controllers, FastEndpoints) |
| `FastEndpoints:EndpointRoutePrefix` | FastEndpoints only | Applied specifically to FastEndpoints endpoints |

---

## Custom FastEndpoints Configuration

Implement `IFastEndpointsConfigurator` to customize the FastEndpoints configuration per shell:

```csharp
using CShells.FastEndpoints;
using FastEndpoints;
using System.Text.Json;

public class MyFastEndpointsConfigurator : IFastEndpointsConfigurator
{
    public void Configure(Config config)
    {
        config.Serializer.Options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        config.Serializer.Options.WriteIndented = false;
    }
}
```

Register it in your feature's `ConfigureServices`:

```csharp
public class ProductsFeature : IFastEndpointsShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<IFastEndpointsConfigurator, MyFastEndpointsConfigurator>();
    }
}
```

---

## Registration in `Program.cs`

No special registration is needed for FastEndpoints beyond `AddShells()` and `MapShells()`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddShells();

var app = builder.Build();
app.MapShells();  // Registers shell middleware and all FastEndpoints

app.Run();
```

---

## Related Packages

| Package | Use When |
|---|---|
| `CShells.FastEndpoints` | Main application project |
| `CShells.FastEndpoints.Abstractions` | Feature class library (lightweight) |
