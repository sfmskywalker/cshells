using Microsoft.Extensions.DependencyInjection;

namespace CShells.Lifecycle;

/// <summary>
/// Extension methods for registering shell lifecycle components.
/// </summary>
public static class ServiceCollectionLifecycleExtensions
{
    /// <summary>
    /// Registers a transient shell initializer in <see cref="LifecyclePhase.Default"/> with order <c>0</c>.
    /// </summary>
    /// <typeparam name="TInitializer">The initializer implementation type.</typeparam>
    /// <param name="services">The shell service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This is the first-class equivalent of registering a transient
    /// <see cref="IShellInitializer"/> directly, with explicit lifecycle metadata attached.
    /// The initializer runs in <see cref="LifecyclePhase.Default"/> after any
    /// <see cref="LifecyclePhase.Prepare"/> initializers and before any
    /// <see cref="LifecyclePhase.Start"/> initializers.
    /// <code>
    /// services.AddShellInitializer&lt;WarmCacheInitializer&gt;();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddShellInitializer<TInitializer>(this IServiceCollection services)
        where TInitializer : class, IShellInitializer =>
        services.AddShellInitializer<TInitializer>(LifecyclePhase.Default, order: 0);

    /// <summary>
    /// Registers a transient shell initializer in <see cref="LifecyclePhase.Default"/>.
    /// </summary>
    /// <typeparam name="TInitializer">The initializer implementation type.</typeparam>
    /// <param name="services">The shell service collection to register into.</param>
    /// <param name="order">The numeric order within <see cref="LifecyclePhase.Default"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Use this overload when an initializer should stay in the compatibility phase but run
    /// before or after other default-phase initializers.
    /// <code>
    /// services.AddShellInitializer&lt;WarmCacheInitializer&gt;(order: 100);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddShellInitializer<TInitializer>(
        this IServiceCollection services,
        int order)
        where TInitializer : class, IShellInitializer =>
        services.AddShellInitializer<TInitializer>(LifecyclePhase.Default, order);

    /// <summary>
    /// Registers a transient shell initializer in the specified lifecycle phase.
    /// </summary>
    /// <typeparam name="TInitializer">The initializer implementation type.</typeparam>
    /// <param name="services">The shell service collection to register into.</param>
    /// <param name="phase">The semantic lifecycle phase.</param>
    /// <param name="order">The numeric order within <paramref name="phase"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Initializers registered through this API are resolved from the shell's
    /// <see cref="IServiceProvider"/> at activation time, so they may depend on shell-scoped
    /// services. Explicit metadata from this registration overrides any
    /// <see cref="LifecycleOrderAttribute"/> on <typeparamref name="TInitializer"/>.
    /// <typeparamref name="TInitializer"/> is registered as transient and exposed as
    /// <see cref="IShellInitializer"/> without replacing any existing descriptors.
    /// <code>
    /// services.AddShellInitializer&lt;ApplyMigrationsInitializer&gt;(LifecyclePhase.Prepare, order: 100);
    /// services.AddShellInitializer&lt;StartSchedulerInitializer&gt;(LifecyclePhase.Start, order: 100);
    /// </code>
    /// As an alternative for legacy registrations, apply
    /// <see cref="LifecycleOrderAttribute"/> to the initializer implementation type.
    /// </remarks>
    public static IServiceCollection AddShellInitializer<TInitializer>(
        this IServiceCollection services,
        LifecyclePhase phase,
        int order)
        where TInitializer : class, IShellInitializer
    {
        Guard.Against.Null(services);

        services.AddTransient<TInitializer>();
        services.AddTransient<IShellInitializer>(sp => sp.GetRequiredService<TInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(TInitializer),
            phase,
            order,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source: $"AddShellInitializer<{typeof(TInitializer).FullName}>"));

        return services;
    }
}
