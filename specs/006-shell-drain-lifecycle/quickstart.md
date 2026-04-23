# Quickstart: Shell Generations, Reload & Disposal Lifecycle

This guide shows the key usage patterns for the `IShellRegistry` API. Hosts register a
**blueprint** per shell name; the library owns generation assignment, activation, reload,
and cooperative drain.

---

## 1. Register CShells with blueprints and drain policy

```csharp
// Program.cs
builder.Services.AddCShells(cshells =>
{
    cshells.AddShell("payments", shell => shell
        .WithFeature<PaymentsFeature>()
        .WithConfiguration("stripe:key", builder.Configuration["Stripe:ApiKey"]!));

    cshells.AddShell("reporting", shell => shell
        .WithFeature<ReportingFeature>());

    // Optional: override the default 30-second drain policy.
    cshells.ConfigureDrainPolicy(new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(60)));

    // Optional: override the default 3-second grace period.
    cshells.ConfigureGracePeriod(TimeSpan.FromSeconds(5));
});
```

Blueprints can also be loaded from configuration (e.g. `Shells/payments.json`); the
`ConfigurationShellBlueprint` wraps a bound section and recomposes on every reload.

When no drain policy is configured, the default is `FixedTimeoutDrainPolicy(30 seconds)`.

The library's built-in startup hosted service activates every registered blueprint in
parallel at host start. A blueprint that fails to compose, build, or initialize causes host
startup to fail — the same loud-failure contract as any other startup step.

---

## 2. Contribute initializers and drain handlers from features

```csharp
internal sealed class PaymentsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services, ShellFeatureContext context)
    {
        services.AddSingleton<IPaymentProcessor, StripePaymentProcessor>();
        services.AddTransient<IShellInitializer, PaymentsInitializer>();
        services.AddTransient<IDrainHandler, PaymentsDrainHandler>();
    }
}

// Runs once during Initializing → Active.
internal sealed class PaymentsInitializer(IPaymentCache cache) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken ct) => cache.WarmAsync(ct);
}

// Runs during Draining, after all request scopes release (or deadline elapses).
internal sealed class PaymentsDrainHandler(IPaymentProcessor processor) : IDrainHandler
{
    public async Task DrainAsync(IDrainExtensionHandle extensionHandle, CancellationToken ct)
    {
        if (processor.InFlightCount > 0)
            extensionHandle.TryExtend(TimeSpan.FromSeconds(15), out _);

        await processor.WaitForInFlightAsync(ct);
    }
}
```

Initializers run sequentially in the order their features' `ConfigureServices` calls
registered them. Drain handlers run in parallel.

---

## 3. Reload a shell (rolling update)

Update the blueprint's underlying source (fluent delegate capture or `Shells/*.json`) and
call `ReloadAsync`:

```csharp
public async Task DeployNewConfigurationAsync(IShellRegistry registry, CancellationToken ct)
{
    var result = await registry.ReloadAsync("payments", ct);

    if (result.Error is not null)
    {
        logger.LogError(result.Error, "Reload failed for {Name}", result.Name);
        return;
    }

    // result.NewShell.Descriptor.Generation == previous + 1
    // result.NewShell is now Active; the prior generation is draining in the background.

    if (result.Drain is not null)
    {
        var drainResult = await result.Drain.WaitAsync(ct);
        logger.LogInformation(
            "Old generation drained. Status={Status}, ScopeWait={ScopeWait}, Handlers={Count}",
            drainResult.Status,
            drainResult.ScopeWaitElapsed,
            drainResult.HandlerResults.Count);
    }
}
```

Concurrent `ReloadAsync` calls for the same name are serialized; generation numbers are
assigned in arrival order and the last call to complete becomes the active generation.

---

## 4. Reload every shell at once

```csharp
var results = await registry.ReloadAllAsync(ct);

foreach (var r in results)
{
    if (r.Error is not null)
        logger.LogError(r.Error, "Reload failed for {Name}", r.Name);
    else
        logger.LogInformation("Reloaded {Name} to generation {Gen}",
            r.Name, r.NewShell!.Descriptor.Generation);
}
```

One failing blueprint does not abort the batch — every other name still reloads.

---

## 5. Serve requests through a shell scope (web middleware pattern)

```csharp
public sealed class ShellMiddleware(RequestDelegate next, IShellRegistry registry, IShellResolver resolver)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var shellName = resolver.Resolve(context);
        if (shellName is null)
        {
            await next(context);
            return;
        }

        var shell = registry.GetActive(shellName)
            ?? throw new InvalidOperationException($"No active shell for '{shellName}'");

        await using var scope = shell.BeginScope();
        context.RequestServices = scope.ServiceProvider;
        await next(context);
    }
}
```

`shell.BeginScope()` both creates a DI scope and increments the shell's active-scope
counter. When a reload triggers drain, the registry waits for every outstanding scope to
release (bounded by the drain deadline) before running `IDrainHandler` services and
disposing the provider. In-flight requests finish cleanly against the old generation's
provider — no `ObjectDisposedException`s.

---

## 6. Observe lifecycle events

```csharp
public sealed class ShellMonitor(IShellRegistry registry, ILogger<ShellMonitor> logger)
    : IShellLifecycleSubscriber
{
    public void StartObserving() => registry.Subscribe(this);

    public Task OnStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Shell {Descriptor} transitioned {Previous} → {Current}",
            shell.Descriptor, // formats as "payments#3"
            previous,
            current);

        return Task.CompletedTask;
    }
}
```

The library automatically registers a structured-logging subscriber; no manual wiring is
required for basic log output of every generation's transitions.

---

## 7. Configure drain timeout policies

```csharp
// Fixed timeout (default, 30 s)
services.AddSingleton<IDrainPolicy>(new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(30)));

// Extensible: start at 30 s, allow handlers to extend up to 2 minutes total
services.AddSingleton<IDrainPolicy>(new ExtensibleTimeoutDrainPolicy(
    initial: TimeSpan.FromSeconds(30),
    cap: TimeSpan.FromMinutes(2)));

// Unbounded (development/test only — logs a warning on each drain)
services.AddSingleton<IDrainPolicy>(new UnboundedDrainPolicy());
```

Or, equivalently, via the builder: `cshells.ConfigureDrainPolicy(...)`.

---

## 8. Force-complete a drain

```csharp
var reload = await registry.ReloadAsync("payments", ct);

if (reload.Drain is not null)
{
    // Later — e.g. SIGTERM received with no time to wait.
    await reload.Drain.ForceAsync(ct);
    var result = await reload.Drain.WaitAsync(ct); // Status == DrainStatus.Forced
}
```

`ForceAsync` cancels the scope-wait phase immediately as well, so forcing during phase 1
transitions straight to handler cancellation with no further waiting.

---

## State transition summary

```
ActivateAsync  →  Initializing → Active
                    │              │
                    └─ initializers run sequentially before promoting

ReloadAsync    →  (new)  Initializing → Active
                  (old)  Active → Deactivating → Draining → Drained → Disposed
                                                   │
                                                   ├─ phase 1: scope wait
                                                   ├─ phase 2: drain handlers (parallel)
                                                   └─ phase 3: grace after deadline / force

DrainAsync     →  Active | Deactivating → Draining → Drained → Disposed

Emergency path →  Any non-terminal → Disposed  (registry-only, on host shutdown timeout breach)
```

`IShell` does not expose `IAsyncDisposable`; hosts never dispose shells directly. Disposal
is owned by `IShellRegistry` and happens automatically after drain completes, or via the
emergency path on shutdown-timeout breach.

The diagram applies per generation. Multiple generations for the same name can coexist:
exactly one `Active`, any number `Deactivating` / `Draining` / `Drained`.
