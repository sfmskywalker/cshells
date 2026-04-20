using CShells.Configuration;

namespace CShells.Tests.Unit.Configuration;

/// <summary>
/// Tests for the targeted <see cref="IShellSettingsProvider.GetShellSettingsAsync(ShellId, CancellationToken)"/> overload
/// across all built-in provider implementations.
/// </summary>
public class ShellSettingsProviderLookupTests
{
    #region InMemoryShellSettingsProvider

    [Fact]
    public async Task InMemory_GetByShellId_ReturnsMatchingShell()
    {
        var shells = new List<ShellSettings>
        {
            new(new("Alpha"), ["Feature1"]),
            new(new("Beta"), ["Feature2"])
        };
        var provider = new InMemoryShellSettingsProvider(shells);

        var result = await provider.GetShellSettingsAsync(new ShellId("Alpha"));

        Assert.NotNull(result);
        Assert.Equal(new("Alpha"), result.Id);
    }

    [Fact]
    public async Task InMemory_GetByShellId_ReturnsNullWhenNotFound()
    {
        var shells = new List<ShellSettings>
        {
            new(new("Alpha"), ["Feature1"])
        };
        var provider = new InMemoryShellSettingsProvider(shells);

        var result = await provider.GetShellSettingsAsync(new ShellId("Missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task InMemory_GetByShellId_IsCaseInsensitive()
    {
        var shells = new List<ShellSettings>
        {
            new(new("Alpha"), ["Feature1"])
        };
        var provider = new InMemoryShellSettingsProvider(shells);

        var result = await provider.GetShellSettingsAsync(new ShellId("alpha"));

        Assert.NotNull(result);
    }

    #endregion

    #region MutableInMemoryShellSettingsProvider

    [Fact]
    public async Task Mutable_GetByShellId_ReturnsMatchingShell()
    {
        var provider = new MutableInMemoryShellSettingsProvider();
        provider.AddOrUpdate(new(new("Alpha"), ["Feature1"]));
        provider.AddOrUpdate(new(new("Beta"), ["Feature2"]));

        var result = await provider.GetShellSettingsAsync(new ShellId("Alpha"));

        Assert.NotNull(result);
        Assert.Equal(new("Alpha"), result.Id);
    }

    [Fact]
    public async Task Mutable_GetByShellId_ReturnsNullWhenNotFound()
    {
        var provider = new MutableInMemoryShellSettingsProvider();
        provider.AddOrUpdate(new(new("Alpha"), ["Feature1"]));

        var result = await provider.GetShellSettingsAsync(new ShellId("Missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Mutable_GetByShellId_ReflectsRuntimeChanges()
    {
        var provider = new MutableInMemoryShellSettingsProvider();

        // Initially missing
        var result1 = await provider.GetShellSettingsAsync(new ShellId("Dynamic"));
        Assert.Null(result1);

        // Add it
        provider.AddOrUpdate(new(new("Dynamic"), ["Feature1"]));
        var result2 = await provider.GetShellSettingsAsync(new ShellId("Dynamic"));
        Assert.NotNull(result2);

        // Remove it
        provider.Remove(new("Dynamic"));
        var result3 = await provider.GetShellSettingsAsync(new ShellId("Dynamic"));
        Assert.Null(result3);
    }

    #endregion

    #region CompositeShellSettingsProvider

    [Fact]
    public async Task Composite_GetByShellId_ReturnsFromLastProvider()
    {
        var provider1 = new InMemoryShellSettingsProvider([
            new(new("Shared"), ["Feature1"])
        ]);
        var provider2 = new InMemoryShellSettingsProvider([
            new(new("Shared"), ["Feature2"])
        ]);
        var composite = new CompositeShellSettingsProvider([provider1, provider2]);

        var result = await composite.GetShellSettingsAsync(new ShellId("Shared"));

        Assert.NotNull(result);
        Assert.Equal(["Feature2"], result.EnabledFeatures);
    }

    [Fact]
    public async Task Composite_GetByShellId_ReturnsNullWhenNoProviderHasShell()
    {
        var provider1 = new InMemoryShellSettingsProvider([
            new(new("Alpha"), ["Feature1"])
        ]);
        var composite = new CompositeShellSettingsProvider([provider1]);

        var result = await composite.GetShellSettingsAsync(new ShellId("Missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Composite_GetByShellId_ReturnsFromSingleProvider()
    {
        var provider1 = new InMemoryShellSettingsProvider([
            new(new("Alpha"), ["Feature1"])
        ]);
        var provider2 = new InMemoryShellSettingsProvider([
            new(new("Beta"), ["Feature2"])
        ]);
        var composite = new CompositeShellSettingsProvider([provider1, provider2]);

        var result = await composite.GetShellSettingsAsync(new ShellId("Alpha"));

        Assert.NotNull(result);
        Assert.Equal(new("Alpha"), result.Id);
        Assert.Equal(["Feature1"], result.EnabledFeatures);
    }

    #endregion
}
