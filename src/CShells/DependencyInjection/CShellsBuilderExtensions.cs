using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Resolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.DependencyInjection;

/// <summary>
/// Extension methods for configuring the CShellsBuilder.
/// </summary>
public static class CShellsBuilderExtensions
{
    /// <summary>
    /// Adds a shell settings provider to the provider pipeline.
    /// Multiple providers can be registered and will be queried in registration order.
    /// </summary>
    /// <typeparam name="TProvider">The type of the shell settings provider.</typeparam>
    /// <param name="builder">The CShells builder.</param>
    /// <returns>The updated CShells builder.</returns>
    public static CShellsBuilder WithProvider<TProvider>(this CShellsBuilder builder)
        where TProvider : class, IShellSettingsProvider
    {
        Guard.Against.Null(builder);

        builder.RegisterProvider((sp, providers) =>
        {
            var provider = ActivatorUtilities.CreateInstance<TProvider>(sp);
            providers.Add(provider);
        });

        return builder;
    }

    /// <summary>
    /// Adds a shell settings provider instance to the provider pipeline.
    /// Multiple providers can be registered and will be queried in registration order.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="provider">The shell settings provider instance.</param>
    /// <returns>The updated CShells builder.</returns>
    public static CShellsBuilder WithProvider(this CShellsBuilder builder, IShellSettingsProvider provider)
    {
        Guard.Against.Null(builder);
        Guard.Against.Null(provider);

        builder.RegisterProvider((_, providers) =>
        {
            providers.Add(provider);
        });

        return builder;
    }

    /// <summary>
    /// Adds a shell settings provider using a factory function to the provider pipeline.
    /// Multiple providers can be registered and will be queried in registration order.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="factory">The factory function to create the shell settings provider.</param>
    /// <returns>The updated CShells builder.</returns>
    public static CShellsBuilder WithProvider(this CShellsBuilder builder, Func<IServiceProvider, IShellSettingsProvider> factory)
    {
        Guard.Against.Null(builder);
        Guard.Against.Null(factory);

        builder.RegisterProvider((sp, providers) =>
        {
            var provider = factory(sp)
                ?? throw new InvalidOperationException("The shell settings provider factory returned null.");
            providers.Add(provider);
        });

        return builder;
    }

    /// <summary>
    /// Adds the configuration-based shell settings provider to the provider pipeline.
    /// Multiple providers can be registered and will be queried in registration order.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section name (default: "CShells").</param>
    /// <returns>The updated CShells builder.</returns>
    public static CShellsBuilder WithConfigurationProvider(this CShellsBuilder builder, IConfiguration configuration,
        string sectionName = CShellsOptions.SectionName)
    {
        Guard.Against.Null(builder);
        Guard.Against.Null(configuration);

        builder.RegisterProvider((_, providers) =>
        {
            providers.Add(new ConfigurationShellSettingsProvider(configuration, sectionName));
        });

        return builder;
    }

    /// <summary>
    /// Appends an explicit assembly contribution for shell feature discovery.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="assemblies">The assemblies to scan for shell features.</param>
    /// <returns>The updated CShells builder.</returns>
    /// <remarks>
    /// <para>
    /// Every call appends another provider registration. Later calls do not replace earlier ones.
    /// </para>
    /// <para>
    /// Passing an empty array is valid and still switches CShells into explicit feature-assembly provider mode.
    /// </para>
    /// </remarks>
    public static CShellsBuilder FromAssemblies(this CShellsBuilder builder, params Assembly[] assemblies)
    {
        Guard.Against.Null(builder);

        var configuredAssemblies = Guard.Against.Null(assemblies)
            .Select((assembly, index) => assembly ?? throw new ArgumentException($"Explicit feature assembly at index {index} cannot be null.", nameof(assemblies)))
            .ToArray();

        builder.RegisterFeatureAssemblyProvider(_ => new ExplicitFeatureAssemblyProvider(configuredAssemblies));

        return builder;
    }

    /// <summary>
    /// Appends the assembly containing <typeparamref name="TMarker"/> as an explicit assembly contribution for shell feature discovery.
    /// </summary>
    /// <typeparam name="TMarker">Any marker type defined in the assembly to scan for shell features.</typeparam>
    /// <param name="builder">The CShells builder.</param>
    /// <returns>The updated CShells builder.</returns>
    /// <remarks>
    /// This is a convenience wrapper over <see cref="FromAssemblies(CShellsBuilder,System.Reflection.Assembly[])"/> for the common marker-type pattern.
    /// </remarks>
    public static CShellsBuilder FromAssemblyContaining<TMarker>(this CShellsBuilder builder) =>
        FromAssemblies(builder, typeof(TMarker).Assembly);

    /// <summary>
    /// Appends the built-in host-derived assembly contribution for shell feature discovery.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <returns>The updated CShells builder.</returns>
    /// <remarks>
    /// The host-derived assembly set matches the default feature discovery behavior used when no assembly-source methods are called.
    /// </remarks>
    public static CShellsBuilder FromHostAssemblies(this CShellsBuilder builder)
    {
        Guard.Against.Null(builder);

        builder.RegisterFeatureAssemblyProvider(_ => new HostFeatureAssemblyProvider());

        return builder;
    }

    /// <summary>
    /// Appends a custom feature assembly provider that will be resolved from the root application service provider.
    /// </summary>
    /// <typeparam name="TProvider">The custom provider type.</typeparam>
    /// <param name="builder">The CShells builder.</param>
    /// <returns>The updated CShells builder.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TProvider"/> cannot be resolved from the root application service provider.
    /// </exception>
    public static CShellsBuilder WithAssemblyProvider<TProvider>(this CShellsBuilder builder)
        where TProvider : class, IFeatureAssemblyProvider
    {
        Guard.Against.Null(builder);

        builder.RegisterFeatureAssemblyProvider(sp => sp.GetService<TProvider>()
            ?? throw new InvalidOperationException($"The feature assembly provider '{typeof(TProvider).FullName}' could not be resolved from the root application service provider. Register it before calling WithAssemblyProvider<TProvider>()."));

        return builder;
    }

    /// <summary>
    /// Appends a custom feature assembly provider instance.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="provider">The custom provider instance.</param>
    /// <returns>The updated CShells builder.</returns>
    public static CShellsBuilder WithAssemblyProvider(this CShellsBuilder builder, IFeatureAssemblyProvider provider)
    {
        Guard.Against.Null(builder);

        var featureAssemblyProvider = Guard.Against.Null(provider);
        builder.RegisterFeatureAssemblyProvider(_ => featureAssemblyProvider);

        return builder;
    }

    /// <summary>
    /// Appends a custom feature assembly provider using a factory evaluated against the root application service provider.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="factory">The factory used to create the custom provider.</param>
    /// <returns>The updated CShells builder.</returns>
    public static CShellsBuilder WithAssemblyProvider(this CShellsBuilder builder, Func<IServiceProvider, IFeatureAssemblyProvider> factory)
    {
        Guard.Against.Null(builder);

        var providerFactory = Guard.Against.Null(factory);
        builder.RegisterFeatureAssemblyProvider(sp => providerFactory(sp)
            ?? throw new InvalidOperationException("The feature assembly provider factory returned null. Return a non-null provider instance."));

        return builder;
    }

    /// <summary>
    /// Configures the shell resolver strategy pipeline with explicit control over which strategies
    /// are registered and their execution order.
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <param name="configure">Configuration action for the resolver pipeline.</param>
    /// <returns>The updated CShells builder.</returns>
    /// <remarks>
    /// When this method is called, it replaces the default resolver strategy registration behavior.
    /// Use this for advanced scenarios where you need full control over the resolver pipeline.
    /// For common scenarios, consider using convenience methods like <c>WithWebRouting</c> or <c>WithDefaultResolver</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddCShells(shells => shells
    ///     .ConfigureResolverPipeline(pipeline => pipeline
    ///         .Use&lt;WebRoutingShellResolver&gt;(order: 0)
    ///         .Use&lt;ClaimsBasedResolver&gt;(order: 50)
    ///         .UseFallback&lt;DefaultShellResolverStrategy&gt;()
    ///     )
    /// );
    /// </code>
    /// </example>
    public static CShellsBuilder ConfigureResolverPipeline(this CShellsBuilder builder, Action<ResolverPipelineBuilder> configure)
    {
        Guard.Against.Null(builder);
        Guard.Against.Null(configure);

        var pipelineBuilder = new ResolverPipelineBuilder(builder.Services);
        configure(pipelineBuilder);
        pipelineBuilder.Build();

        return builder;
    }

    /// <summary>
    /// Configures the shell resolver to use the default fallback strategy.
    /// This strategy always resolves to a shell with Id "Default".
    /// </summary>
    /// <param name="builder">The CShells builder.</param>
    /// <returns>The updated CShells builder.</returns>
    /// <remarks>
    /// This is a convenience method that configures the resolver pipeline with just the <see cref="DefaultShellResolverStrategy"/>.
    /// It's typically used in non-web scenarios where simple default shell resolution is sufficient.
    /// </remarks>
    public static CShellsBuilder WithDefaultResolver(this CShellsBuilder builder)
    {
        Guard.Against.Null(builder);

        return builder.ConfigureResolverPipeline(pipeline => pipeline
            .Use<DefaultShellResolverStrategy>());
    }
}
