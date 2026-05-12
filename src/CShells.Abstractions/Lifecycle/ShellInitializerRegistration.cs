namespace CShells.Lifecycle;

/// <summary>
/// Ordering metadata for an <see cref="IShellInitializer"/> registration.
/// </summary>
/// <param name="InitializerType">Concrete initializer implementation type.</param>
/// <param name="Phase">Semantic lifecycle phase.</param>
/// <param name="Order">Numeric order within <paramref name="Phase"/>.</param>
/// <param name="RegistrationIndex">Optional registration index used for diagnostics.</param>
/// <param name="IsExplicit">Whether this metadata came from an explicitly ordered lifecycle API.</param>
/// <param name="Source">Human-readable metadata source for diagnostics.</param>
/// <remarks>
/// <see cref="ServiceCollectionLifecycleExtensions.AddShellInitializer{TInitializer}(Microsoft.Extensions.DependencyInjection.IServiceCollection, LifecyclePhase, int)"/>
/// registers initializer implementations with transient lifetime. Existing unordered
/// <see cref="IShellInitializer"/> registrations do not need this metadata and are treated as
/// <see cref="LifecyclePhase.Default"/> entries.
/// </remarks>
public sealed record ShellInitializerRegistration(
    Type InitializerType,
    LifecyclePhase Phase,
    int Order,
    int RegistrationIndex,
    bool IsExplicit,
    string Source);
