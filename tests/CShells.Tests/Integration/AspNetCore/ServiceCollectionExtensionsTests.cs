using CShells.DependencyInjection;
using CShells.Resolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Integration.AspNetCore;

/// <summary>
/// Tests for <see cref="CShells.AspNetCore.Extensions.ServiceCollectionExtensions"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact(DisplayName = "AddCShellsAspNetCore registers default IShellResolver")]
    public void AddCShellsAspNetCore_RegistersDefaultResolver()
    {
        using var sp = BuildProvider();
        Assert.NotNull(sp.GetService<IShellResolver>());
    }

    [Fact(DisplayName = "AddCShellsAspNetCore default resolver returns null when no active shells exist")]
    public void AddCShellsAspNetCore_DefaultResolver_ReturnsNullWithoutShells()
    {
        using var sp = BuildProvider();
        var resolver = sp.GetRequiredService<IShellResolver>();

        var result = resolver.Resolve(new ShellResolutionContext());

        Assert.Null(result);
    }

    [Fact(DisplayName = "AddCShellsAspNetCore does not override a custom IShellResolver")]
    public void AddCShellsAspNetCore_WithCustomResolver_DoesNotOverride()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IShellResolver, CustomShellResolver>();
        CShells.AspNetCore.Extensions.ServiceCollectionExtensions.AddCShellsAspNetCore(services);

        using var sp = services.BuildServiceProvider();

        Assert.IsType<CustomShellResolver>(sp.GetRequiredService<IShellResolver>());
    }

    [Fact(DisplayName = "AddCShellsAspNetCore with null services throws ArgumentNullException")]
    public void AddCShellsAspNetCore_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        var ex = Assert.Throws<ArgumentNullException>(() =>
            CShells.AspNetCore.Extensions.ServiceCollectionExtensions.AddCShellsAspNetCore(services!));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact(DisplayName = "AddCShellsAspNetCore returns CShellsBuilder for chaining")]
    public void AddCShellsAspNetCore_ReturnsBuilderForChaining()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var result = CShells.AspNetCore.Extensions.ServiceCollectionExtensions.AddCShellsAspNetCore(services);

        Assert.IsType<CShellsBuilder>(result);
        Assert.Same(services, result.Services);
    }

    [Fact(DisplayName = "AddCShellsAspNetCore registers web-routing + default fallback + any custom strategies")]
    public void AddCShellsAspNetCore_RegistersStrategies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IShellResolverStrategy, CustomStrategy>();
        CShells.AspNetCore.Extensions.ServiceCollectionExtensions.AddCShellsAspNetCore(services);

        using var sp = services.BuildServiceProvider();
        var strategies = sp.GetServices<IShellResolverStrategy>().ToList();

        Assert.Contains(strategies, s => s is CustomStrategy);
        Assert.Contains(strategies, s => s.GetType().Name == "WebRoutingShellResolver");
        Assert.Contains(strategies, s => s is DefaultShellResolverStrategy);
    }

    [Fact(DisplayName = "DefaultShellResolver orchestrates strategies in order and returns the first non-null hit")]
    public void DefaultShellResolver_OrchestratesInOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IShellResolverStrategy, NullStrategy>();
        services.AddSingleton<IShellResolverStrategy, CustomStrategy>();
        CShells.AspNetCore.Extensions.ServiceCollectionExtensions.AddCShellsAspNetCore(services);

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IShellResolver>();

        var result = resolver.Resolve(new ShellResolutionContext());

        Assert.NotNull(result);
        Assert.Equal(new ShellId("Custom"), result.Value);
    }

    [Fact(DisplayName = "AddCShellsAspNetCore invokes configure exactly once")]
    public void AddCShellsAspNetCore_CallsConfigureOnce()
    {
        var counter = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        CShells.AspNetCore.Extensions.ServiceCollectionExtensions.AddCShellsAspNetCore(services, _ => counter++);

        Assert.Equal(1, counter);
    }

    private static ServiceProvider BuildProvider(Action<CShellsBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        CShells.AspNetCore.Extensions.ServiceCollectionExtensions.AddCShellsAspNetCore(services, configure);
        return services.BuildServiceProvider();
    }

    private sealed class CustomShellResolver : IShellResolver
    {
        public ShellId? Resolve(ShellResolutionContext context) => new("Custom");
    }

    private sealed class CustomStrategy : IShellResolverStrategy
    {
        public ShellId? Resolve(ShellResolutionContext context) => new("Custom");
    }

    private sealed class NullStrategy : IShellResolverStrategy
    {
        public ShellId? Resolve(ShellResolutionContext context) => null;
    }
}
