# Quickstart: Single-Provider Blueprint Simplification

**Feature**: [008-single-provider-simplification](spec.md)
**Audience**: host developers integrating CShells `008+`. If you're upgrading
from `007`, this is a small migration with two outcomes: either zero code changes
(single-source hosts) or one fail-fast error to fix at startup (mixed-source
hosts).

## 1. Pick a source

Every CShells host has exactly one blueprint source. Three shapes:

### a) Code-first (most common)

```csharp
builder.Services.AddCShells(c => c
    .AddShell("payments", s => s.WithFeature<PaymentsFeature>())
    .AddShell("billing",  s => s.WithFeature<BillingFeature>()));
```

`AddShell(...)` is shorthand for "register this blueprint with the built-in
in-memory provider." No further registration needed. Identical to `007`.

### b) Configuration-backed

```csharp
builder.Services.AddCShells(c => c.WithConfigurationProvider(builder.Configuration));
```

`appsettings.json`:

```json
{
  "CShells": {
    "Shells": {
      "tenant-acme":    { "Features": ["Billing", "Notifications"] },
      "tenant-widgets": { "Features": ["Billing"] }
    }
  }
}
```

Identical to `007`.

### c) Storage-backed (or any custom provider)

```csharp
builder.Services.AddCShells(c => c.WithFluentStorageBlueprints(blobStorage));
// — or your own implementation — //
builder.Services.AddCShells(c => c.AddBlueprintProvider(sp => new MySqlShellBlueprintProvider(connStr)));
```

Identical to `007`.

## 2. Don't mix sources

This is the **only behavioral change** in `008`:

```csharp
// ❌ Throws at startup in 008 (silently worked in 007 via the composite)
builder.Services.AddCShells(c => c
    .AddShell("platform", s => s.WithFeature<PlatformFeature>())
    .WithConfigurationProvider(builder.Configuration));
```

The framework throws an `InvalidOperationException` during composition with this
message (paraphrased):

> `AddShell` registers blueprints with the in-memory provider, but
> `AddBlueprintProvider`-registered providers are external — and exactly one
> provider is permitted. Either:
>
> - Move your `AddShell` blueprints into the configuration source, or
> - Drop `WithConfigurationProvider` and keep `AddShell`, or
> - Implement a custom `IShellBlueprintProvider` that combines both sources.

Likewise, registering two external providers fails:

```csharp
// ❌ Throws — exactly one external provider permitted
builder.Services.AddCShells(c => c
    .WithConfigurationProvider(builder.Configuration)
    .WithFluentStorageBlueprints(blobStorage));
```

## 3. Migration paths for mixed-source hosts

If your `007` host did the mixed-source thing, pick one:

| Want | Do |
|------|-----|
| Code-defined "platform" shell + tenants in config | Add a `platform` entry to `appsettings.json`, drop the `AddShell("platform", ...)` call |
| Code-defined "platform" shell + tenants in blob | Use `manager.CreateAsync(new ShellSettings(new ShellId("platform")))` once at deployment to seed the blob, drop the `AddShell` call |
| Genuinely need both as separate sources | Implement your own `IShellBlueprintProvider` that fans out to both internally; register it via `AddBlueprintProvider`. The framework no longer ships a composite; you own the policy |

## 4. Implementing your own provider

The provider contract is unchanged from `007` and is the open extension point:

```csharp
public sealed class MyCustomShellBlueprintProvider : IShellBlueprintProvider
{
    public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken ct) { ... }
    public Task<BlueprintPage> ListAsync(BlueprintListQuery q, CancellationToken ct) { ... }
    // optional override:
    public Task<bool> ExistsAsync(string name, CancellationToken ct) { ... }
}
```

Register it:

```csharp
services.AddSingleton<MyCustomShellBlueprintProvider>();
builder.Services.AddCShells(c =>
    c.AddBlueprintProvider(sp => sp.GetRequiredService<MyCustomShellBlueprintProvider>()));
```

If your source supports mutation, the same class can also implement
`IShellBlueprintManager` and pair it with the blueprint via `ProvidedBlueprint`.

## 5. What disappeared from the public API

- `DuplicateBlueprintException` — gone. The error condition (two providers
  claim the same name) is no longer reachable in the single-provider model.

That's the entire public-API delta. Everything else is internal cleanup.

## 6. Routing, lazy activation, eviction, drain — all unchanged

`GetOrActivateAsync`, the per-name semaphore, drain/dispose semantics, the
`ShellMiddleware` 404/503 translations, `ListAsync` pagination — every behavior
specified in `007` continues to work identically.
