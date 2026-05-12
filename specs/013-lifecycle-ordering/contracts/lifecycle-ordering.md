# Contract: Lifecycle Ordering

## Feature Dependency Contract

Feature dependencies keep their current meaning:

- `ShellFeatureAttribute.DependsOn` controls service configuration order only.
- Dependency features configure services before dependent features.
- Lifecycle ordering is not inferred from `DependsOn`.
- Feature constructors still resolve only root-level services plus allowed shell metadata.

## Initializer Registration Contract

Feature authors register ordered initializers from `ConfigureServices`:

```csharp
public sealed class QuartzPostgreSqlFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddShellInitializer<RunQuartzPostgreSqlMigrations>(
            LifecyclePhase.Prepare,
            order: 100);
    }
}

public sealed class QuartzFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddShellInitializer<StartQuartzScheduler>(
            LifecyclePhase.Start,
            order: 100);
    }
}
```

Contract rules:

- `AddShellInitializer<TInitializer>()` registers `TInitializer` as a transient `IShellInitializer`.
- `AddShellInitializer<TInitializer>(order)` registers a transient initializer with explicit numeric lifecycle order in `LifecyclePhase.Default`.
- `AddShellInitializer<TInitializer>(phase, order)` registers a transient initializer with semantic phase and numeric order.
- `TInitializer` must implement `IShellInitializer`.
- Initializer instances are resolved from the shell service provider at execution time.
- Existing `services.AddTransient<IShellInitializer, TInitializer>()` registrations remain valid and unordered.
- Unordered registrations run in `LifecyclePhase.Default`, after `Prepare` and before `Start`, while keeping DI registration order relative to other unordered registrations.
- Explicit registration metadata overrides initializer type metadata.
- Null services, invalid types, or invalid metadata fail with argument validation or activation diagnostics.

## Attribute Metadata Contract

Reusable initializer types may declare default lifecycle metadata:

```csharp
[LifecycleOrder(LifecyclePhase.Prepare, order: 100)]
public sealed class RunQuartzPostgreSqlMigrations : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // migration work
        return Task.CompletedTask;
    }
}
```

Contract rules:

- Attribute metadata is read from the initializer implementation type.
- Attribute metadata does not require constructing the initializer.
- Explicit registration metadata takes precedence over attribute metadata.
- Attribute-only initializers registered through the existing DI pattern may participate in lifecycle ordering.

## Ordering Contract

Execution order is deterministic:

1. Phase order executes as `Prepare` → `Default` → `Start`.
2. Within a phase, lower numeric order values execute earlier.
3. Explicit registration metadata wins over attribute metadata.
4. Unordered initializers occupy `Default`.
5. Equal effective phase/order values use registration index as the deterministic tie-break.
6. Initializers run sequentially.
7. If an initializer throws, activation fails and the shell is not promoted to Active.

Diagnostics:

- Invalid metadata fails before any initializer runs.
- Before/after relationship declarations are not supported by this feature.
- Equal order ambiguity may be logged but does not fail when registration-index tie-break is available.
- Error diagnostics identify the shell and involved initializer type names.

## Drain Handler Contract

Drain behavior remains unchanged:

- `IDrainHandler` registrations continue to run in parallel.
- Drain deadlines, force-drain cancellation, grace-period behavior, and `DrainResult` reporting remain unchanged.
- Ordered or phased drain execution is deferred and is not part of this feature.

## Provider/Base Feature Contract

Provider/base feature pairs use both contracts together:

```csharp
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
```

Expected behavior:

- `QuartzFeature.ConfigureServices` runs before `QuartzPostgreSqlFeature.ConfigureServices`.
- `RunQuartzPostgreSqlMigrations.InitializeAsync` runs before `StartQuartzScheduler.InitializeAsync`.
- The provider feature does not reverse `DependsOn`.
- The provider feature does not manually start the base feature or replace its service registrations.
