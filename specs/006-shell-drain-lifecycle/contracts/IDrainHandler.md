# Contract: IDrainHandler

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IDrainHandler` is the host-extensibility point for cooperative drain. Hosts register one or more
implementations to perform graceful-shutdown work (e.g., drain a message bus, wait for active
workflows) before the shell's service provider is disposed.

## Interface Definitions

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Performs cooperative drain work when a shell enters
/// <see cref="ShellLifecycleState.Draining"/>.
/// </summary>
/// <remarks>
/// Register implementations as transient services in
/// <see cref="IShellFeature.ConfigureServices"/>. All handlers are resolved from the draining
/// shell's <see cref="IServiceProvider"/> and invoked in parallel.
/// </remarks>
public interface IDrainHandler
{
    /// <summary>
    /// Performs drain work for this handler.
    /// </summary>
    /// <param name="extensionHandle">
    /// Handle to request a deadline extension from the configured <see cref="IDrainPolicy"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancelled when the drain deadline elapses or
    /// <see cref="IDrainOperation.ForceAsync"/> is called. Handlers should observe this
    /// token and return promptly when cancelled.
    /// </param>
    Task DrainAsync(IDrainExtensionHandle extensionHandle, CancellationToken cancellationToken);
}

/// <summary>
/// Allows a drain handler to request a deadline extension from the active
/// <see cref="IDrainPolicy"/>.
/// </summary>
public interface IDrainExtensionHandle
{
    /// <summary>
    /// Requests that the drain deadline be extended by <paramref name="requested"/>.
    /// </summary>
    /// <param name="requested">The additional time requested.</param>
    /// <param name="granted">
    /// The actual extension granted by the policy, which may be less than
    /// <paramref name="requested"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the policy granted at least some extension; <c>false</c> if the policy
    /// rejects all extensions (e.g., <see cref="FixedTimeoutDrainPolicy"/>).
    /// </returns>
    bool TryExtend(TimeSpan requested, out TimeSpan granted);
}
```

## Behaviour Contract

- Handlers are resolved fresh from the shell's `IServiceProvider` on each drain.
- All handlers are invoked concurrently via `Task.WhenAll` (or equivalent).
- A handler that throws stores the exception in `DrainHandlerResult.Error`; it does **not** abort
  other handlers or prevent the shell from reaching `Drained`.
- A handler that does not complete within the deadline has its `CancellationToken` cancelled;
  its `DrainHandlerResult.Completed` is `false`.
- After the configured grace period, handlers still running are abandoned (not awaited) and the
  shell transitions to `Drained`.

## Registration Example

```csharp
public class WorkflowFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<IDrainHandler, WorkflowDrainHandler>();
    }
}

internal sealed class WorkflowDrainHandler(IWorkflowEngine engine) : IDrainHandler
{
    public async Task DrainAsync(IDrainExtensionHandle extensionHandle, CancellationToken cancellationToken)
    {
        // Request an extension if needed
        if (engine.ActiveCount > 0)
            extensionHandle.TryExtend(TimeSpan.FromSeconds(10), out _);

        await engine.WaitForAllAsync(cancellationToken);
    }
}
```
