# Quickstart: Shell Draining & Disposal Lifecycle

This guide shows the key usage patterns for the new `IShellRegistry` API.

---

## 1. Register CShells with drain support

```csharp
// Program.cs
builder.Services.AddCShells(cshells =>
{
    // Optional: override the default 30-second drain policy
    cshells.ConfigureDrainPolicy(new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(60)));
});
```

When no drain policy is configured, the default is `FixedTimeoutDrainPolicy(30 seconds)`.

---

## 2. Create and promote a shell

```csharp
public class ShellManager(IShellRegistry registry)
{
    public async Task<IShell> StartPaymentsShellAsync(CancellationToken ct)
    {
        // Create in Initializing state
        var shell = await registry.CreateAsync(
            name: "payments",
            version: "v1",
            configure: services =>
            {
                services.AddSingleton<IPaymentProcessor, StripePaymentProcessor>();
                services.AddTransient<IDrainHandler, PaymentDrainHandler>();
            },
            metadata: new Dictionary<string, string> { ["owner"] = "payments-team" },
            cancellationToken: ct);

        // Promote to Active — this is when GetActive("payments") starts returning this shell
        await registry.PromoteAsync(shell, ct);
        return shell;
    }
}
```

---

## 3. Register a drain handler

```csharp
internal sealed class PaymentDrainHandler(IPaymentProcessor processor) : IDrainHandler
{
    public async Task DrainAsync(IDrainExtensionHandle extensionHandle, CancellationToken cancellationToken)
    {
        // Optionally request more time if there are in-flight transactions
        if (processor.InFlightCount > 0)
            extensionHandle.TryExtend(TimeSpan.FromSeconds(15), out _);

        await processor.WaitForInFlightAsync(cancellationToken);
    }
}

// Registered in the shell's service collection:
// services.AddTransient<IDrainHandler, PaymentDrainHandler>();
```

---

## 4. Replace an active shell (rolling update)

```csharp
public async Task DeployNewVersionAsync(IShellRegistry registry, CancellationToken ct)
{
    // Create the new version in Initializing state
    var newShell = await registry.CreateAsync("payments", "v2", services =>
    {
        services.AddSingleton<IPaymentProcessor, StripePaymentProcessorV2>();
        services.AddTransient<IDrainHandler, PaymentDrainHandler>();
    }, cancellationToken: ct);

    // ReplaceAsync: promotes v2 AND initiates drain on v1 atomically
    var drainOp = await registry.ReplaceAsync(newShell, ct);

    if (drainOp is not null)
    {
        // Await drain completion (optional — drain continues in background regardless)
        var result = await drainOp.WaitAsync(ct);

        logger.LogInformation(
            "Old shell drained. Status={Status}, Handlers={Count}",
            result.Status,
            result.HandlerResults.Count);
    }
}
```

---

## 5. Observe lifecycle events

```csharp
public class ShellMonitor(IShellRegistry registry, ILogger<ShellMonitor> logger)
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
            "Shell {Name}@{Version} transitioned {Previous} → {Current}",
            shell.Descriptor.Name,
            shell.Descriptor.Version,
            previous,
            current);

        return Task.CompletedTask;
    }
}
```

The library automatically registers a structured-logging subscriber; no manual wiring is required
for basic log output.

---

## 6. Configure drain timeout policies

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

---

## 7. Force-complete a drain

```csharp
var drainOp = await registry.DrainAsync(shell, ct);

// Later — e.g. SIGTERM received with no time to wait
await drainOp.ForceAsync(ct);
var result = await drainOp.WaitAsync(ct); // Status == DrainStatus.Forced
```

---

## State transition summary

```
CreateAsync  →  Initializing
PromoteAsync →  Active
ReplaceAsync →  (new shell) Active, (old shell) Deactivating → Draining → Drained → Disposed
DrainAsync   →  Draining → Drained → Disposed
DisposeAsync →  Disposed (any state, skips drain)
```
