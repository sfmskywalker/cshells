# Shell Lifecycle

CShells builds one isolated service provider per shell generation. Lifecycle APIs let features run startup work after the provider is built and cooperative shutdown work while an old generation drains.

## Runtime States

A shell generation moves through:

```text
Initializing -> Active -> Deactivating -> Draining -> Drained -> Disposed
```

`IShellInitializer` instances run while the shell is `Initializing`, before the generation is published as `Active`. `IDrainHandler` instances run while the shell is `Draining`, after outstanding `IShellScope` handles finish or the drain deadline is reached.

## Initializer Ordering

Feature dependencies and initializer order are separate concepts:

- `[ShellFeature(DependsOn = ...)]` still means "configure the dependency first".
- Lifecycle ordering controls when `IShellInitializer` instances execute after the shell provider has been built.
- Existing unordered `IShellInitializer` registrations remain valid and run in `LifecyclePhase.Default`.
- Unordered initializers keep DI registration order unless explicit lifecycle metadata is used.

Use `AddShellInitializer<TInitializer>()` for first-class lifecycle metadata:

```csharp
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

[ShellFeature("StorageProvider")]
public sealed class StorageProviderFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddShellInitializer<ApplyStorageMigrations>(
            LifecyclePhase.Prepare,
            order: 100);
    }
}

[ShellFeature("Runtime")]
public sealed class RuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddShellInitializer<StartRuntime>(
            LifecyclePhase.Start,
            order: 100);
    }
}
```

Execution order is deterministic:

1. `LifecyclePhase.Prepare`
2. `LifecyclePhase.Default`
3. `LifecyclePhase.Start`

Within a phase, lower numeric `order` runs first. Equal phase/order ties use DI registration order as a deterministic tie-break and are reported as non-fatal diagnostics.

## Compatibility

Existing registrations continue to work:

```csharp
public sealed class ExistingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<IShellInitializer, FirstInitializer>();
        services.AddTransient<IShellInitializer, SecondInitializer>();
    }
}
```

Both initializers run in `LifecyclePhase.Default`, and `FirstInitializer` still runs before `SecondInitializer`.

`AddShellInitializer<TInitializer>()` registers `TInitializer` as transient and also registers `IShellInitializer` through the shell service provider. Initializers may depend on shell-scoped services, but feature constructors should still only consume root-level services plus supported shell context values.

## Attribute Metadata

When a legacy `IShellInitializer` registration should carry lifecycle metadata without changing the registration call, apply `LifecycleOrderAttribute` to the initializer type:

```csharp
[LifecycleOrder(LifecyclePhase.Prepare, 50)]
public sealed class ApplySchemaInitializer(IMigrationRunner migrations) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        migrations.ApplyAsync(cancellationToken);
}
```

Explicit metadata from `AddShellInitializer<TInitializer>(...)` overrides attribute metadata for the same initializer type.

## Provider/Base Feature Pairs

Provider features should keep depending on base features for service configuration, then use lifecycle phases to run provider preparation before base runtime startup.

```csharp
[ShellFeature("Quartz")]
public sealed class QuartzFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddShellInitializer<StartQuartzScheduler>(
            LifecyclePhase.Start,
            order: 100);
    }
}

[ShellFeature("QuartzPostgreSql", DependsOn = [typeof(QuartzFeature)])]
public sealed class QuartzPostgreSqlFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddShellInitializer<RunQuartzPostgreSqlMigrations>(
            LifecyclePhase.Prepare,
            order: 100);
    }
}
```

Quartz configures first because `QuartzPostgreSqlFeature` depends on `QuartzFeature`. PostgreSQL migrations run first because they are in `Prepare`; the scheduler starts later in `Start`.

## Drain

Drain behavior is intentionally unchanged by initializer ordering. `IDrainHandler` implementations are resolved from the shell provider and invoked in parallel during `Draining`.

Ordered or phased drain execution is deferred to a future design. Existing deadline, force-drain, grace-period, and result-reporting behavior remains the compatibility baseline.

## Diagnostics

CShells fails activation before initializer side effects when explicit lifecycle metadata references an initializer type that is not resolved from DI or does not implement `IShellInitializer`. Exception messages include the shell descriptor and affected initializer type names.

Equal phase/order ties are allowed for compatibility and deterministic execution, but they are surfaced as diagnostics so authors can choose clearer order values when desired.
