# Integration Patterns

This page explains how to safely integrate CShells into existing ASP.NET Core applications and covers the correct middleware ordering and common pitfalls.

---

## Correct Middleware Order

```csharp
var app = builder.Build();

// 1. Exception handling
app.UseExceptionHandler("/Error");

// 2. HTTPS redirection
app.UseHttpsRedirection();

// 3. Static files (served before shell resolution)
app.UseStaticFiles();

// 4. Routing (required before MapShells)
app.UseRouting();

// 5. Authentication and authorization (before MapShells if used)
app.UseAuthentication();
app.UseAuthorization();

// 6. Host application endpoints (register BEFORE MapShells to avoid shadowing)
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => "Host home page");

// 7. Shell middleware and endpoints (last)
app.MapShells();

app.Run();
```

`MapShells()` registers both the shell resolution middleware and all shell endpoints. It **must** be called after `UseRouting()`.

---

## Pattern 1: Dedicated Path Prefixes (Recommended)

Give each shell a unique path prefix to avoid route conflicts:

```json
{
  "CShells": {
    "Shells": [
      {
        "Name": "Acme",
        "Configuration": { "WebRouting": { "Path": "tenants/acme" } }
      },
      {
        "Name": "Contoso",
        "Configuration": { "WebRouting": { "Path": "tenants/contoso" } }
      }
    ]
  }
}
```

- Shell routes: `/tenants/acme/*`, `/tenants/contoso/*`
- Host routes: `/api/*`, `/health`, `/` — no conflicts

---

## Pattern 2: Subdomain-Based Isolation

Use host routing when each tenant has its own subdomain:

```json
{
  "Name": "Acme",
  "Configuration": {
    "WebRouting": { "Host": "acme.example.com" }
  }
}
```

Requests to `acme.example.com` → Acme shell; `www.example.com` → host application.

---

## Pattern 3: Mixed Mode (Host Routes + Shell Routes)

Combine host routes with shell routes by registering host routes first:

```csharp
// Host routes
app.MapGet("/", () => "Host");
app.MapControllers();        // /api/*

// Shell routes
app.MapShells();             // /tenants/* or host-based
```

Register host routes **before** `MapShells()` so they take priority.

---

## Pattern 4: Root-Level Shell

Use an empty path prefix when the entire application is multi-tenant:

```json
{
  "Name": "Default",
  "Configuration": {
    "WebRouting": { "Path": "" }
  }
}
```

> ⚠️ All requests match at the root level. Only use this pattern if your app has no host-level routes that could conflict.

---

## Protecting Host Routes with Path Exclusions

Prevent shell resolution from intercepting paths that belong to the host application:

```csharp
builder.AddShells(cshells =>
{
    cshells.WithFluentStorageProvider(blobStorage);
    cshells.WithWebRouting(options =>
    {
        options.ExcludePaths = ["/api", "/health", "/swagger"];
    });
});
```

Requests to `/api/*`, `/health`, and `/swagger*` are never passed to the shell middleware.

---

## Authentication and Authorization

### Per-Shell Authentication Schemes

Enable shell-aware authentication to allow each shell to define its own authentication schemes:

```csharp
builder.Services.AddAuthentication(); // Call FIRST
builder.Services.AddCShellsAspNetCore(cshells => cshells
    .WithAuthentication()
);
```

Each shell's features can then register authentication schemes in `ConfigureServices`:

```csharp
public class JwtAuthFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthentication()
            .AddJwtBearer(options =>
            {
                // Shell-specific JWT configuration
            });
    }
}
```

### Per-Shell Authorization Policies

```csharp
builder.Services.AddAuthorization();
builder.Services.AddCShellsAspNetCore(cshells => cshells
    .WithAuthorization()
);
```

### Both Together

```csharp
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddCShellsAspNetCore(cshells => cshells
    .WithAuthenticationAndAuthorization()
);
```

Middleware pipeline when using auth:

```csharp
app.UseRouting();
app.UseAuthentication();   // before MapShells
app.UseAuthorization();    // before MapShells
app.MapShells();
```

---

## Full Program.cs Example

```csharp
using FluentStorage;
using CShells.Providers.FluentStorage;

var builder = WebApplication.CreateBuilder(args);

// Host services
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// CShells
var shellsPath = Path.Combine(builder.Environment.ContentRootPath, "Shells");
var blobStorage = StorageFactory.Blobs.DirectoryFiles(shellsPath);

builder.AddShells(cshells =>
{
    cshells.WithFluentStorageProvider(blobStorage);
    cshells.WithWebRouting(options =>
    {
        options.ExcludePaths = ["/api", "/swagger", "/health"];
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Host routes first
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => "Host Home");

// Shell routes
app.MapShells();

app.Run();
```

---

## Troubleshooting

### `AmbiguousMatchException` — Multiple Endpoints Match

**Cause:** Two shells (or a shell and a host route) register the same route pattern.

**Fix:**
1. Check CShells warning logs for conflict messages.
2. Ensure each shell has a unique `WebRouting:Path`.
3. Use `options.ExcludePaths` to protect host routes.

### Host Routes Return 404

**Cause:** A root-level shell (`Path: ""`) is shadowing host routes, or host routes are registered after `MapShells()`.

**Fix:**
1. Register host routes before `MapShells()`.
2. Add host paths to `options.ExcludePaths`.
3. Avoid empty path prefixes unless the entire app is multi-tenant.

### Shell Routes Return 404

**Cause:** `MapShells()` not called, shell configs missing `WebRouting:Path`, or providers not loading.

**Fix:**
1. Ensure `app.MapShells()` is in the pipeline.
2. Check that shell JSON files have `Configuration.WebRouting.Path` set.
3. Check startup logs for `"Loaded N shell(s)"`.
