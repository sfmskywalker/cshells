# Quickstart: Lifecycle Ordering

## Default Compatibility

Existing initializers continue to work:

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

Expected behavior:

- Existing initializers run in `LifecyclePhase.Default`.
- `FirstInitializer` runs before `SecondInitializer`.
- Both initializers run while the shell is still `Initializing`.
- If either initializer throws, activation fails and no active shell is published.

## Explicit Initializer Order

Use the lifecycle registration API when startup side effects need a specific sequence:

```csharp
public sealed class StorageProviderFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddShellInitializer<ApplySchemaMigrations>(
            LifecyclePhase.Prepare,
            order: 100);
    }
}

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

Expected behavior:

- `AddShellInitializer<T>()` registers `T` as transient.
- `Prepare` initializers run before unordered `Default` initializers.
- Unordered `Default` initializers run before `Start` initializers.
- Initializers are still resolved from the shell provider, so they can use shell-scoped services.
- Feature dependency order remains independent from initializer order.
- Before/after relationship declarations are not supported by this feature.

## Quartz-Style Provider/Base Pair

Keep provider dependencies pointed at the base feature for configuration:

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
```

The base feature declares its runtime startup later:

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
```

Expected behavior:

- Quartz services configure first because `QuartzPostgreSqlFeature` depends on `QuartzFeature`.
- PostgreSQL migrations initialize first because their initializer is in the `Prepare` phase.
- The scheduler starts only after migrations and any unordered `Default` initializers complete.

## Drain Behavior

Drain remains parallel:

- `IDrainHandler` execution is unchanged by initializer ordering.
- Ordered or phased drain execution is deferred and requires a separate future design.
- Existing deadline, force-drain, grace-period, and result-reporting behavior remains unchanged.

## Verification

Run focused lifecycle tests while implementing:

```bash
dotnet test tests/CShells.Tests/ --filter "FullyQualifiedName~Lifecycle"
```

Run the complete suite before handing off:

```bash
dotnet test
```

Documentation updates should show:

- `DependsOn` is for service configuration order.
- Lifecycle ordering is for initializer execution order.
- Existing unordered initializer registrations remain valid in `Default`.
- `AddShellInitializer<T>()` uses transient lifetime.
- Drain handlers remain parallel; ordered or phased drain execution is deferred.
