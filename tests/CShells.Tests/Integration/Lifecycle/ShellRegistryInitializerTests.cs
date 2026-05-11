using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace CShells.Tests.Integration.Lifecycle;

/// <summary>
/// Integration tests for US4: <see cref="IShellInitializer"/> invocation during
/// <see cref="IShellRegistry.ActivateAsync"/>.
/// </summary>
public class ShellRegistryInitializerTests
{
    [Fact(DisplayName = "Initializers run sequentially in DI-registration order before the shell becomes Active")]
    public async Task Initializers_RunSequentially_BeforeActive()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<TwoInitializersFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("payments");

        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(["first", "second"], collector.Invocations);
        Assert.All(collector.StatesSeenInsideInitializer, s => Assert.Equal(ShellLifecycleState.Initializing, s));
    }

    [Fact(DisplayName = "Initializer exception propagates and leaves no partial entry")]
    public async Task InitializerException_Propagates_NoPartialEntry()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<ThrowingInitializerFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await Assert.ThrowsAsync<ApplicationException>(() => registry.ActivateAsync("payments"));

        Assert.Null(registry.GetActive("payments"));
        Assert.Empty(registry.GetAll("payments"));
    }

    [Fact(DisplayName = "Feature dependency order and initializer order are independent")]
    public async Task Initializers_CanRunBeforeDependencyInitializer_WithoutChangingFeatureConfigureOrder()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("quartz", s => s.WithFeature<ProviderDependsOnBaseFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("quartz");

        Assert.Equal(["base-configure", "provider-configure"], collector.ConfigureInvocations);
        Assert.Equal(["provider-prepare", "base-start"], collector.Invocations);
    }

    [Fact(DisplayName = "Ordered initializer exception propagates and leaves no partial entry")]
    public async Task OrderedInitializerException_Propagates_NoPartialEntry()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<ThrowingOrderedInitializerFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await Assert.ThrowsAsync<ApplicationException>(() => registry.ActivateAsync("payments"));

        Assert.Null(registry.GetActive("payments"));
        Assert.Empty(registry.GetAll("payments"));
    }

    [Fact(DisplayName = "Unordered initializers run in Default between Prepare and Start")]
    public async Task Initializers_DefaultCompatibility_RunsBetweenPrepareAndStart()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<PhaseOrderingFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("payments");

        Assert.Equal(["prepare", "legacy", "start"], collector.Invocations);
    }

    [Fact(DisplayName = "Attribute metadata orders legacy initializer registrations")]
    public async Task AttributeMetadata_OrdersLegacyInitializerRegistration()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<AttributeOrderedInitializerFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("payments");

        Assert.Equal(["attribute-prepare", "legacy"], collector.Invocations);
    }

    [Fact(DisplayName = "Explicit registration metadata overrides LifecycleOrderAttribute")]
    public async Task ExplicitMetadata_OverridesAttributeMetadata()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<ExplicitOverridesAttributeFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("payments");

        Assert.Equal(["legacy", "attribute-prepare"], collector.Invocations);
    }

    [Fact(DisplayName = "AddShellInitializer resolves a fresh transient initializer per activation")]
    public async Task AddShellInitializer_ResolvesFreshTransientInitializer_PerActivation()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<TransientInitializerFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("payments");
        var reload = await registry.ReloadAsync("payments");
        if (reload.Drain is not null)
            await reload.Drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, collector.InstanceIds.Count);
        Assert.NotEqual(collector.InstanceIds[0], collector.InstanceIds[1]);
    }

    [Fact(DisplayName = "Invalid ordering metadata fails before initializer side effects")]
    public async Task InvalidOrderingMetadata_FailsBeforeInitializerSideEffects()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<InvalidOrderingMetadataFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        var ex = await Assert.ThrowsAsync<ShellInitializerOrderException>(() => registry.ActivateAsync("payments"));

        Assert.Contains(nameof(MissingInitializer), ex.Message);
        Assert.Empty(collector.Invocations);
        Assert.Null(registry.GetActive("payments"));
    }

    [Fact(DisplayName = "Equal phase/order initializers use deterministic registration order")]
    public async Task EqualPhaseAndOrder_UsesRegistrationOrderTieBreak()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("payments", s => s.WithFeature<EqualOrderInitializerFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("payments");

        Assert.Equal(["first", "second"], collector.Invocations);
    }

    [Fact(DisplayName = "Quartz-style provider initializer runs before base scheduler initializer")]
    public async Task QuartzStyleProviderBasePair_ConfiguresBaseFirst_InitializesProviderFirst()
    {
        var collector = new InitOrderCollector();

        await using var host = BuildHostWithCollector(collector, cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("quartz", s => s.WithFeature<QuartzPostgreSqlFeature>()));
        var registry = host.GetRequiredService<IShellRegistry>();

        await registry.ActivateAsync("quartz");

        Assert.Equal(["quartz-configure", "postgres-configure"], collector.ConfigureInvocations);
        Assert.Equal(["postgres-migrations", "quartz-scheduler"], collector.Invocations);
    }

    [Fact(DisplayName = "Shell with no initializers activates immediately")]
    public async Task NoInitializers_ActivatesImmediately()
    {
        await using var host = BuildHost(cshells => cshells
            .WithAssemblyContaining<ShellRegistryInitializerTests>()
            .AddShell("plain", _ => { }));
        var registry = host.GetRequiredService<IShellRegistry>();

        var shell = await registry.ActivateAsync("plain");

        Assert.Equal(ShellLifecycleState.Active, shell.State);
    }

    // =================================================================
    // Test doubles
    // =================================================================

    private static ServiceProvider BuildHostWithCollector(InitOrderCollector collector, Action<CShellsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Register the collector in root services BEFORE AddCShells; shells inherit root
        // registrations, so the initializers resolve it cleanly via their constructor.
        services.AddSingleton(collector);
        services.AddCShells(configure);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildHost(Action<CShellsBuilder> configure) =>
        ShellRegistryActivateTests.BuildHost(configure);

    public sealed class InitOrderCollector
    {
        public List<string> Invocations { get; } = [];
        public List<string> ConfigureInvocations { get; } = [];
        public List<ShellLifecycleState> StatesSeenInsideInitializer { get; } = [];
        public List<int> InstanceIds { get; } = [];
    }

    private sealed class FirstInitializer(IShell shell, InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("first");
            collector.StatesSeenInsideInitializer.Add(shell.State);
            return Task.CompletedTask;
        }
    }

    private sealed class SecondInitializer(IShell shell, InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("second");
            collector.StatesSeenInsideInitializer.Add(shell.State);
            return Task.CompletedTask;
        }
    }

    public sealed class TwoInitializersFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IShellInitializer, FirstInitializer>();
            services.AddTransient<IShellInitializer, SecondInitializer>();
        }
    }

    private sealed class ThrowingInitializer : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => throw new ApplicationException("init fail");
    }

    public sealed class ThrowingInitializerFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IShellInitializer, ThrowingInitializer>();
        }
    }

    private sealed class ThrowingOrderedInitializer : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => throw new ApplicationException("ordered init fail");
    }

    public sealed class ThrowingOrderedInitializerFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddShellInitializer<ThrowingOrderedInitializer>(LifecyclePhase.Prepare, 0);
    }

    [ShellFeature("BaseFeature")]
    public sealed class BaseFeature(InitOrderCollector collector) : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            collector.ConfigureInvocations.Add("base-configure");
            services.AddShellInitializer<BaseStartInitializer>(LifecyclePhase.Start, 0);
        }
    }

    [ShellFeature("ProviderDependsOnBase", DependsOn = [typeof(BaseFeature)])]
    public sealed class ProviderDependsOnBaseFeature(InitOrderCollector collector) : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            collector.ConfigureInvocations.Add("provider-configure");
            services.AddShellInitializer<ProviderPrepareInitializer>(LifecyclePhase.Prepare, 0);
        }
    }

    private sealed class BaseStartInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("base-start");
            return Task.CompletedTask;
        }
    }

    private sealed class ProviderPrepareInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("provider-prepare");
            return Task.CompletedTask;
        }
    }

    public sealed class PhaseOrderingFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddShellInitializer<PrepareInitializer>(LifecyclePhase.Prepare, 0);
            services.AddTransient<IShellInitializer, LegacyInitializer>();
            services.AddShellInitializer<StartInitializer>(LifecyclePhase.Start, 0);
        }
    }

    private sealed class PrepareInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("prepare");
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("legacy");
            return Task.CompletedTask;
        }
    }

    private sealed class StartInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("start");
            return Task.CompletedTask;
        }
    }

    public sealed class AttributeOrderedInitializerFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IShellInitializer, LegacyInitializer>();
            services.AddTransient<IShellInitializer, AttributePrepareInitializer>();
        }
    }

    public sealed class ExplicitOverridesAttributeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IShellInitializer, LegacyInitializer>();
            services.AddShellInitializer<AttributePrepareInitializer>(LifecyclePhase.Start, 0);
        }
    }

    [LifecycleOrder(LifecyclePhase.Prepare, 0)]
    private sealed class AttributePrepareInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("attribute-prepare");
            return Task.CompletedTask;
        }
    }

    public sealed class TransientInitializerFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddShellInitializer<IdentityRecordingInitializer>();
    }

    private sealed class IdentityRecordingInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.InstanceIds.Add(RuntimeHelpers.GetHashCode(this));
            return Task.CompletedTask;
        }
    }

    public sealed class InvalidOrderingMetadataFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(new ShellInitializerRegistration(
                typeof(MissingInitializer),
                LifecyclePhase.Prepare,
                Order: 0,
                RegistrationIndex: -1,
                IsExplicit: true,
                Source: "invalid test metadata"));
            services.AddTransient<IShellInitializer, SideEffectInitializer>();
        }
    }

    private sealed class MissingInitializer : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SideEffectInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("side-effect");
            return Task.CompletedTask;
        }
    }

    public sealed class EqualOrderInitializerFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddShellInitializer<EqualOrderFirstInitializer>(LifecyclePhase.Prepare, 0);
            services.AddShellInitializer<EqualOrderSecondInitializer>(LifecyclePhase.Prepare, 0);
        }
    }

    private sealed class EqualOrderFirstInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class EqualOrderSecondInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("second");
            return Task.CompletedTask;
        }
    }

    [ShellFeature("Quartz")]
    public sealed class QuartzFeature(InitOrderCollector collector) : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            collector.ConfigureInvocations.Add("quartz-configure");
            services.AddShellInitializer<QuartzSchedulerInitializer>(LifecyclePhase.Start, 0);
        }
    }

    [ShellFeature("QuartzPostgreSql", DependsOn = [typeof(QuartzFeature)])]
    public sealed class QuartzPostgreSqlFeature(InitOrderCollector collector) : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            collector.ConfigureInvocations.Add("postgres-configure");
            services.AddShellInitializer<PostgreSqlMigrationInitializer>(LifecyclePhase.Prepare, 0);
        }
    }

    private sealed class QuartzSchedulerInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("quartz-scheduler");
            return Task.CompletedTask;
        }
    }

    private sealed class PostgreSqlMigrationInitializer(InitOrderCollector collector) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            collector.Invocations.Add("postgres-migrations");
            return Task.CompletedTask;
        }
    }
}
