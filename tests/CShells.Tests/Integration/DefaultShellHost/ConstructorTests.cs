using CShells.Configuration;
using CShells.Hosting;
using CShells.Tests.TestHelpers;

namespace CShells.Tests.Integration.DefaultShellHost;

/// <summary>
/// Tests for <see cref="DefaultShellHost"/> constructor validation.
/// </summary>
[Collection(nameof(DefaultShellHostCollection))]
public class ConstructorTests(DefaultShellHostFixture fixture)
{
    [Theory(DisplayName = "Constructor guard clauses throw ArgumentNullException")]
    [InlineData(true, false, false, false, false, false, "shellSettingsCache")]
    [InlineData(false, true, false, false, false, false, "assemblies")]
    [InlineData(false, false, true, false, false, false, "rootProvider")]
    [InlineData(false, false, false, true, false, false, "rootServicesAccessor")]
    [InlineData(false, false, false, false, true, false, "featureFactory")]
    [InlineData(false, false, false, false, false, true, "exclusionRegistry")]
    public void Constructor_GuardClauses_ThrowArgumentNullException(
        bool nullCache,
        bool nullAssemblies,
        bool nullRootProvider,
        bool nullAccessor,
        bool nullFactory,
        bool nullExclusionRegistry,
        string expectedParam)
    {
        var cache = nullCache ? null : new ShellSettingsCache();
        var assemblies = nullAssemblies ? null : Array.Empty<System.Reflection.Assembly>();
        var rootProvider = nullRootProvider ? null : fixture.RootProvider;
        var accessor = nullAccessor ? null : fixture.RootAccessor;
        var factory = nullFactory ? null : fixture.FeatureFactory;
        var exclusionRegistry = nullExclusionRegistry ? null : new ShellServiceExclusionRegistry([]);

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Hosting.DefaultShellHost(cache!, assemblies!, rootProvider!, accessor!, factory!, exclusionRegistry!));
        Assert.Equal(expectedParam, exception.ParamName);
    }

    [Theory(DisplayName = "Deferred constructor guard clauses throw ArgumentNullException")]
    [InlineData(true, false, false, false, false, false, "shellSettingsCache")]
    [InlineData(false, true, false, false, false, false, "assemblyResolver")]
    [InlineData(false, false, true, false, false, false, "rootProvider")]
    [InlineData(false, false, false, true, false, false, "rootServicesAccessor")]
    [InlineData(false, false, false, false, true, false, "featureFactory")]
    [InlineData(false, false, false, false, false, true, "exclusionRegistry")]
    public void DeferredConstructor_GuardClauses_ThrowArgumentNullException(
        bool nullCache,
        bool nullAssemblyResolver,
        bool nullRootProvider,
        bool nullAccessor,
        bool nullFactory,
        bool nullExclusionRegistry,
        string expectedParam)
    {
        var cache = nullCache ? null : new ShellSettingsCache();
        Func<CancellationToken, Task<IReadOnlyCollection<System.Reflection.Assembly>>> assemblyResolver = _ => Task.FromResult<IReadOnlyCollection<System.Reflection.Assembly>>([]);
        var rootProvider = nullRootProvider ? null : fixture.RootProvider;
        var accessor = nullAccessor ? null : fixture.RootAccessor;
        var factory = nullFactory ? null : fixture.FeatureFactory;
        var exclusionRegistry = nullExclusionRegistry ? null : new ShellServiceExclusionRegistry([]);

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Hosting.DefaultShellHost(cache!, nullAssemblyResolver ? null! : assemblyResolver, rootProvider!, accessor!, factory!, exclusionRegistry!));
        Assert.Equal(expectedParam, exception.ParamName);
    }

    [Fact(DisplayName = "Deferred constructor does not expose applied runtimes before reconciliation")]
    public void DeferredConstructor_BeforeInitialize_ThrowsKeyNotFoundException()
    {
        var cache = new ShellSettingsCache();
        cache.Load([new(new("Default"), ["Weather"])]);
        var exclusionRegistry = new ShellServiceExclusionRegistry([]);
        var host = new Hosting.DefaultShellHost(
            cache,
            _ => Task.FromResult<IReadOnlyCollection<System.Reflection.Assembly>>([typeof(ShellHost.TestFixtures).Assembly]),
            fixture.RootProvider,
            fixture.RootAccessor,
            fixture.FeatureFactory,
            exclusionRegistry);

        var exception = Assert.Throws<KeyNotFoundException>(() => host.GetShell(new("Default")));

        Assert.Contains("does not have a committed applied runtime", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Deferred constructor builds shells after the runtime manager reconciles desired state")]
    public async Task DeferredConstructor_AfterManagerInitialization_BuildsShells()
    {
        var cache = new ShellSettingsCache();
        var settings = new ShellSettings(new("Default"), ["Weather"]);
        cache.Load([settings]);
        var exclusionRegistry = new ShellServiceExclusionRegistry([]);
        var host = new Hosting.DefaultShellHost(
            cache,
            _ => Task.FromResult<IReadOnlyCollection<System.Reflection.Assembly>>([typeof(ShellHost.TestFixtures).Assembly]),
            fixture.RootProvider,
            fixture.RootAccessor,
            fixture.FeatureFactory,
            exclusionRegistry);
        var manager = new Management.DefaultShellManager(
            host,
            cache,
            new InMemoryShellSettingsProvider([settings]),
            new Notifications.DefaultNotificationPublisher(fixture.RootProvider));

        await host.InitializeAsync();
        await manager.InitializeRuntimeAsync();

        var shell = host.GetShell(new("Default"));

        Assert.Equal("Default", shell.Id.Name);
    }
}
