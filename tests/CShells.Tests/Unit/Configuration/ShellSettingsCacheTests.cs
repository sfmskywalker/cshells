using CShells.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CShells.Tests.Unit.Configuration;

/// <summary>
/// Tests for <see cref="ShellSettingsCache"/> and <see cref="ShellSettingsCacheInitializer"/>.
/// </summary>
public class ShellSettingsCacheTests
{
    [Fact(DisplayName = "GetAll returns empty collection when cache is empty")]
    public void GetAll_WhenCacheEmpty_ReturnsEmpty()
    {
        // Arrange
        var cache = CreateCache();

        // Act
        var result = cache.GetAll();

        // Assert
        Assert.Empty(result);
    }

    [Theory(DisplayName = "GetById returns null when shell is missing")]
    [InlineData(false)]
    [InlineData(true)]
    public void GetById_WhenShellMissing_ReturnsNull(bool preloadDifferentShell)
    {
        // Arrange
        var cache = preloadDifferentShell
            ? CreateCache(CreateShell("Existing"))
            : CreateCache();

        // Act
        var result = cache.GetById(new("Target"));

        // Assert
        Assert.Null(result);
    }

    [Fact(DisplayName = "Load populates cache with shells")]
    public void Load_PopulatesCacheWithShells()
    {
        // Arrange
        var shells = new[]
        {
            CreateShell("Shell1")
        };
        var cache = new ShellSettingsCache();

        // Act
        cache.Load(shells);

        // Assert
        var result = cache.GetById(new("Shell1"));
        Assert.NotNull(result);
        Assert.Equal(new("Shell1"), result.Id);
    }

    [Fact(DisplayName = "Clear removes all shells from cache")]
    public void Clear_RemovesAllShells()
    {
        // Arrange
        var cache = CreateCache(CreateShell("Shell1"));

        // Act
        cache.Clear();

        // Assert
        var result = cache.GetAll();
        Assert.Empty(result);
    }

    private static ShellSettingsCache CreateCache(params ShellSettings[] shells)
    {
        var cache = new ShellSettingsCache();
        if (shells.Length > 0)
        {
            cache.Load(shells);
        }

        return cache;
    }

    private static ShellSettings CreateShell(string id) => new()
    {
        Id = new(id),
        EnabledFeatures = []
    };
}

public class ShellSettingsCacheInitializerTests
{
    [Fact(DisplayName = "Initializer loads shells from provider into cache")]
    public async Task Initializer_LoadsShellsFromProvider()
    {
        // Arrange
        var provider = new TestShellSettingsProvider([
            new(new("Default")),
            new(new("Contoso"), ["Core"]) 
        ]);
        var cache = new ShellSettingsCache();
        var initializer = new ShellSettingsCacheInitializer(provider, cache, NullLogger<ShellSettingsCacheInitializer>.Instance);

        // Act
        await initializer.StartAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            cache.GetAll(),
            shell => Assert.Equal(new("Default"), shell.Id),
            shell => Assert.Equal(new("Contoso"), shell.Id));
    }

    [Fact(DisplayName = "Initializer replaces existing cached shells with provider state")]
    public async Task Initializer_ReplacesExistingCacheContents()
    {
        // Arrange
        var provider = new TestShellSettingsProvider([new(new("Tenant2"), ["FeatureB"])]);
        var cache = new ShellSettingsCache();
        cache.Load([new(new("Tenant1"), ["FeatureA"])]);
        var initializer = new ShellSettingsCacheInitializer(provider, cache, NullLogger<ShellSettingsCacheInitializer>.Instance);

        // Act
        await initializer.StartAsync(CancellationToken.None);

        // Assert
        var allShells = cache.GetAll();
        Assert.Single(allShells);
        Assert.Equal(new("Tenant2"), allShells.Single().Id);
    }

    private sealed class TestShellSettingsProvider(IReadOnlyCollection<ShellSettings> shells) : IShellSettingsProvider
    {
        public Task<IEnumerable<ShellSettings>> GetShellSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<ShellSettings>>(shells);

        public Task<ShellSettings?> GetShellSettingsAsync(ShellId shellId, CancellationToken cancellationToken = default) =>
            Task.FromResult(shells.FirstOrDefault(shell => shell.Id.Equals(shellId)));
    }
}
