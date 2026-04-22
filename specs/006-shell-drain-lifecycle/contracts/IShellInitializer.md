# Contract: IShellInitializer

**Namespace**: `CShells.Lifecycle`
**Project**: `CShells.Abstractions`

## Purpose

`IShellInitializer` is the host-extensibility point for per-shell activation work. Hosts
contribute implementations through a feature's `ConfigureServices`; the registry resolves
them from the newly-built shell's provider and awaits each one in order during the shell's
transition from `Initializing` to `Active`.

Replaces the legacy `IShellActivatedHandler`.

## Interface Definition

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Performs one-time setup work for a shell as part of its
/// <see cref="ShellLifecycleState.Initializing"/> → <see cref="ShellLifecycleState.Active"/>
/// transition.
/// </summary>
/// <remarks>
/// Register implementations via <see cref="IShellFeature.ConfigureServices"/> on the shell's
/// <see cref="IServiceCollection"/>. The registry resolves
/// <c>IEnumerable&lt;IShellInitializer&gt;</c> from the newly-built provider and awaits each
/// initializer sequentially in DI-registration order before promoting the shell to
/// <see cref="ShellLifecycleState.Active"/>.
/// </remarks>
public interface IShellInitializer
{
    /// <summary>
    /// Performs initialization work.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token linked to the activating registry call.
    /// </param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
```

## Behaviour Contract

- Resolved fresh from the shell's provider at activation time; register as transient.
- Invoked **sequentially** in DI-registration order. This matches `IHostedService.StartAsync`
  behaviour and lets feature authors control ordering via the order their features'
  `ConfigureServices` methods run.
- If any initializer throws, the exception propagates out of
  `IShellRegistry.ActivateAsync` / `ReloadAsync`. The partial shell never reaches `Active`;
  its service provider is disposed; no registry entry is retained.
- The shell's scope counter is zero during initialization — `BeginScope` from *inside* an
  initializer is legal (useful for DI-scoped setup) and counts toward scope-wait if drain
  were to start mid-initialization (which cannot happen — drain only targets `Active`
  shells).

## Registration Example

```csharp
public class PaymentsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services, ShellFeatureContext context)
    {
        services.AddTransient<IShellInitializer, PaymentsInitializer>();
    }
}

internal sealed class PaymentsInitializer(IPaymentCache cache) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
        => cache.WarmAsync(cancellationToken);
}
```
