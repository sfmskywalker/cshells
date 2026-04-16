# Public Contract: Runtime Feature Catalog Refresh

## Feature Assembly Provider Contract

Feature assembly providers already participate in discovery through `IFeatureAssemblyProvider`. Under this feature, they also participate in repeated runtime catalog refreshes.

This public interface remains in `CShells.Abstractions`.

```csharp
namespace CShells.Features;

public interface IFeatureAssemblyProvider
{
    Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
}
```

### Behavioral Rules

- Providers may be called multiple times during the application lifetime, not only once during startup.
- Each call must return a non-null sequence of assemblies representing the provider's current discoverable contribution.
- Returning an empty sequence is allowed.
- Providers must not assume that previously returned assemblies remain the committed runtime catalog input for future refreshes.
- Providers should be safe for repeated invocation during single-shell and full-shell reconciliation.

## Runtime Feature Catalog Contract

The runtime feature catalog is an implementation-level snapshot in `CShells`, but it exposes externally meaningful behavior because `IFeatureAssemblyProvider` results and shell reconciliation depend on it.

### Behavioral Rules

- Every startup, single-shell reload, and full-shell reload operation refreshes the runtime feature catalog before shell candidate evaluation begins.
- Catalog refresh builds a candidate snapshot from all configured assembly-provider sources before changing the committed catalog.
- The candidate snapshot is committed only if feature discovery succeeds and every discovered feature ID is unique.
- If refresh detects duplicate feature IDs or another catalog-level inconsistency, the refresh fails explicitly and the previously committed catalog remains in effect.
- A failed catalog refresh aborts the reconciliation operation before any applied shell runtime is changed.

## Shell Candidate Evaluation Contract

Shell reconciliation consumes only the committed feature catalog snapshot from the current refresh pass.

### Behavioral Rules

- Missing required features in the committed catalog produce `DeferredDueToMissingFeatures`, not silent desired-state mutation.
- Non-missing-feature build or validation problems produce `Failed`.
- A shell candidate runtime becomes routable only after it is fully built, validated, and atomically committed.
- A previously applied runtime remains active while a newer desired generation is deferred or failed.
- Once a later catalog refresh makes a deferred desired generation satisfiable, reconciliation builds a fresh candidate runtime against the newly committed catalog and may promote it in the same pass.

