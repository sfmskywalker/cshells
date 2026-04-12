# Public Contract: Feature Assembly Provider Selection

## Scope

This contract defines the public API and behavioral rules for configuring CShells feature-discovery assemblies through the fluent builder model.

The contract applies to:

- `IServiceCollection.AddCShells(...)`
- `IServiceCollection.AddCShellsAspNetCore(...)`
- `WebApplicationBuilder.AddShells(...)`
- `CShellsBuilder` fluent configuration for feature-discovery sources
- third-party implementations of the public feature-assembly provider abstraction

## Public Abstraction Contract

The custom extension point is a public interface that lives in `CShells.Abstractions`.

```csharp
namespace CShells.Features;

public interface IFeatureAssemblyProvider
{
    IEnumerable<Assembly> GetAssemblies(IServiceProvider serviceProvider);
}
```

### Behavioral Rules

- Implementations may return zero or more assemblies, but they must return a non-null sequence.
- The `serviceProvider` argument is the root application service provider available to the shell registration pipeline.
- Implementations must be usable alongside built-in providers in the same discovery flow.
- Returning an assembly already contributed by another provider is allowed; CShells deduplicates before feature discovery.
- Returning `null` is invalid and must fail fast with actionable guidance.
- The contract is part of the supported public extension surface and is not an internal-only seam.

## Fluent Builder Contract

The `CShellsBuilder` public surface exposes fluent assembly-source APIs with additive semantics.

```csharp
public static class CShellsBuilderExtensions
{
    public static CShellsBuilder FromAssemblies(this CShellsBuilder builder, params Assembly[] assemblies);
    public static CShellsBuilder FromHostAssemblies(this CShellsBuilder builder);
    public static CShellsBuilder WithAssemblyProvider<TProvider>(this CShellsBuilder builder)
        where TProvider : class, IFeatureAssemblyProvider;
    public static CShellsBuilder WithAssemblyProvider(this CShellsBuilder builder, IFeatureAssemblyProvider provider);
    public static CShellsBuilder WithAssemblyProvider(
        this CShellsBuilder builder,
        Func<IServiceProvider, IFeatureAssemblyProvider> factory);
}
```

### Behavioral Rules

- Every assembly-source method call appends another provider registration to the builder-managed provider list.
- Later calls do not replace earlier calls.
- `FromAssemblies(...)` appends a built-in provider that contributes exactly the configured assemblies for that call.
- `FromHostAssemblies()` appends a built-in provider that contributes the same host-derived assembly set used by today’s default behavior.
- `WithAssemblyProvider(...)` appends a custom provider that participates in the same additive flow as built-in providers.
- `WithAssemblyProvider<TProvider>()` resolves `TProvider` from the root application service provider and fails fast with actionable guidance if that provider cannot be resolved.
- Null builder arguments, null provider instances, and null provider factories must be rejected with actionable errors.
- Empty explicit assembly lists are valid and still switch discovery into explicit-provider mode.
- Custom providers receive the same root application service provider context described by `IFeatureAssemblyProvider.GetAssemblies(...)`.

## Discovery-Mode Contract

### Default Behavior

If no assembly-source method is called, CShells must preserve current default behavior by scanning the host-derived assembly set.

### Explicit Behavior

Once any assembly-source method is called:

- CShells must use only explicitly configured providers.
- Host-derived assemblies are not included implicitly.
- Host-derived assemblies are included only if `FromHostAssemblies()` was appended.

## Aggregate Discovery Contract

Before calling `FeatureDiscovery`, CShells must:

1. evaluate the selected provider list in registration order,
2. concatenate all returned assemblies,
3. deduplicate the aggregate set, and
4. scan the resulting distinct set exactly once.

### Behavioral Rules

- Built-in and custom providers compose additively.
- Duplicate assembly contributions do not change discovery results.
- Provider-registration order is preserved for aggregation and diagnostics even though final scanning uses a deduplicated union.
- An explicit-mode configuration may legitimately produce an empty discovery set.
- A custom provider that returns `null` is treated as invalid input and does not produce a partial discovery result.

## Breaking-Change Contract

The previous non-fluent assembly-argument approach is removed.

### Required Outcomes

- Public examples must use fluent assembly-source configuration only.
- Legacy `AddCShells` / `AddShells` overloads that accept assemblies as direct method arguments are not retained as supported API.
- Migration guidance must map legacy assembly-argument examples to the new fluent equivalents.

