using System.Reflection;
using CShells.Configuration;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using CShells.Resolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CShells.DependencyInjection;

/// <summary>
/// Extension methods for configuring the CShellsBuilder.
/// </summary>
public static class CShellsBuilderExtensions
{
    /// <summary>
    /// Registers every shell defined in the given <see cref="IConfiguration"/> section as a
    /// <see cref="ConfigurationShellBlueprint"/>. The default section name is
    /// <see cref="CShellsOptions.SectionName"/> (<c>CShells</c>).
    /// </summary>
    public static CShellsBuilder WithConfigurationProvider(
        this CShellsBuilder builder,
        IConfiguration configuration,
        string sectionName = CShellsOptions.SectionName)
    {
        Guard.Against.Null(builder);
        Guard.Against.Null(configuration);

        var shellsSection = configuration.GetSection(sectionName).GetSection("Shells");
        foreach (var childSection in shellsSection.GetChildren())
        {
            // Each child either has a "Name" property or exposes the shell name as its key.
            var name = childSection["Name"] ?? childSection.Key;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            builder.AddBlueprint(new ConfigurationShellBlueprint(name, childSection));
        }

        return builder;
    }

    /// <summary>
    /// Appends an explicit assembly contribution for shell feature discovery.
    /// </summary>
    /// <remarks>
    /// Every call appends another provider registration. Later calls do not replace earlier ones.
    /// Passing an empty array is valid and still switches CShells into explicit feature-assembly provider mode.
    /// </remarks>
    public static CShellsBuilder WithAssemblies(this CShellsBuilder builder, params Assembly[] assemblies)
    {
        Guard.Against.Null(builder);

        var configuredAssemblies = Guard.Against.Null(assemblies)
            .Select((assembly, index) => assembly ?? throw new ArgumentException($"Explicit feature assembly at index {index} cannot be null.", nameof(assemblies)))
            .ToArray();

        builder.RegisterFeatureAssemblyProvider(_ => new ExplicitFeatureAssemblyProvider(configuredAssemblies));

        return builder;
    }

    /// <summary>
    /// Appends the assembly containing <typeparamref name="TMarker"/> as an explicit assembly contribution.
    /// </summary>
    public static CShellsBuilder WithAssemblyContaining<TMarker>(this CShellsBuilder builder) =>
        builder.WithAssemblies(typeof(TMarker).Assembly);

    /// <summary>
    /// Appends the built-in host-derived assembly contribution for shell feature discovery.
    /// </summary>
    public static CShellsBuilder WithHostAssemblies(this CShellsBuilder builder)
    {
        Guard.Against.Null(builder);
        builder.RegisterFeatureAssemblyProvider(_ => new HostFeatureAssemblyProvider());
        return builder;
    }

    /// <summary>
    /// Appends a custom feature assembly provider resolved from the root service provider.
    /// </summary>
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
    public static CShellsBuilder WithAssemblyProvider(this CShellsBuilder builder, Func<IServiceProvider, IFeatureAssemblyProvider> factory)
    {
        Guard.Against.Null(builder);

        var providerFactory = Guard.Against.Null(factory);
        builder.RegisterFeatureAssemblyProvider(sp => providerFactory(sp)
            ?? throw new InvalidOperationException("The feature assembly provider factory returned null. Return a non-null provider instance."));

        return builder;
    }

    /// <summary>
    /// Configures the shell resolver strategy pipeline with explicit control over which
    /// strategies are registered and their execution order.
    /// </summary>
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
    /// Configures the shell resolver to use the default fallback strategy — always resolves to
    /// a shell with Id <c>Default</c> (or the first active shell if <c>Default</c> isn't active).
    /// </summary>
    public static CShellsBuilder WithDefaultResolver(this CShellsBuilder builder)
    {
        Guard.Against.Null(builder);

        return builder.ConfigureResolverPipeline(pipeline => pipeline
            .Use<DefaultShellResolverStrategy>());
    }

    /// <summary>
    /// Overrides the default <see cref="IDrainPolicy"/> (<c>FixedTimeoutDrainPolicy(30s)</c>)
    /// applied to every drain in the registry.
    /// </summary>
    public static CShellsBuilder ConfigureDrainPolicy(this CShellsBuilder builder, IDrainPolicy policy)
    {
        Guard.Against.Null(builder);
        Guard.Against.Null(policy);

        builder.Services.Replace(ServiceDescriptor.Singleton(policy));
        return builder;
    }

    /// <summary>
    /// Overrides the default drain grace period (3 seconds) applied after the drain deadline
    /// elapses or <see cref="IDrainOperation.ForceAsync"/> is called.
    /// </summary>
    public static CShellsBuilder ConfigureGracePeriod(this CShellsBuilder builder, TimeSpan gracePeriod)
    {
        Guard.Against.Null(builder);
        if (gracePeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(gracePeriod), "Grace period must be positive.");

        builder.Services.Replace(ServiceDescriptor.Singleton(new DrainGracePeriod(gracePeriod)));
        return builder;
    }
}
