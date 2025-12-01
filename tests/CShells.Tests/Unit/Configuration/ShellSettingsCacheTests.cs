using CShells.Configuration;
using CShells.Management;
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
        var cache = new ShellSettingsCache();

        // Act
        var result = cache.GetAll();

        // Assert
        Assert.Empty(result);
    }

    [Fact(DisplayName = "GetById returns null when cache is empty")]
    public void GetById_WhenCacheEmpty_ReturnsNull()
    {
        // Arrange
        var cache = new ShellSettingsCache();

        // Act
        var result = cache.GetById(new ShellId("Shell1"));

        // Assert
        Assert.Null(result);
    }

    [Fact(DisplayName = "Initializer loads shells from provider into cache")]
    public async Task Initializer_LoadsShellsFromProvider()
    {
        // Arrange
        var shellManager = new TestShellManager();
        var initializer = new ShellSettingsCacheInitializer(shellManager, NullLogger<ShellSettingsCacheInitializer>.Instance);

        // Act
        await initializer.StartAsync(CancellationToken.None);

        // Assert
        Assert.True(shellManager.ReloadAllShellsAsyncCalled);
    }

    [Fact(DisplayName = "Load populates cache with shells")]
    public void Load_PopulatesCacheWithShells()
    {
        // Arrange
        var shells = new List<ShellSettings>
        {
            new() { Id = new ShellId("Shell1"), EnabledFeatures = [] }
        };
        var cache = new ShellSettingsCache();

        // Act
        cache.Load(shells);

        // Assert
        var result = cache.GetById(new ShellId("Shell1"));
        Assert.NotNull(result);
        Assert.Equal(new ShellId("Shell1"), result.Id);
    }

    [Fact(DisplayName = "GetById returns null when shell not found")]
    public void GetById_WhenShellDoesNotExist_ReturnsNull()
    {
        // Arrange
        var cache = new ShellSettingsCache();
        cache.Load([]);

        // Act
        var result = cache.GetById(new ShellId("NonExistent"));

        // Assert
        Assert.Null(result);
    }

    [Fact(DisplayName = "Initializer StopAsync completes successfully")]
    public async Task Initializer_StopAsync_CompletesSuccessfully()
    {
        // Arrange
        var shellManager = new TestShellManager();
        var initializer = new ShellSettingsCacheInitializer(shellManager, NullLogger<ShellSettingsCacheInitializer>.Instance);
        await initializer.StartAsync(CancellationToken.None);

        // Act & Assert - just verify it completes without throwing
        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact(DisplayName = "Clear removes all shells from cache")]
    public void Clear_RemovesAllShells()
    {
        // Arrange
        var shells = new List<ShellSettings>
        {
            new() { Id = new ShellId("Shell1"), EnabledFeatures = [] }
        };
        var cache = new ShellSettingsCache();
        cache.Load(shells);

        // Act
        cache.Clear();

        // Assert
        var result = cache.GetAll();
        Assert.Empty(result);
    }

    private class TestShellManager : IShellManager
    {
        public bool ReloadAllShellsAsyncCalled { get; private set; }

        public Task ReloadAllShellsAsync(CancellationToken cancellationToken = default)
        {
            ReloadAllShellsAsyncCalled = true;
            return Task.CompletedTask;
        }

        public Task AddShellAsync(ShellSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task RemoveShellAsync(ShellId shellId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task UpdateShellAsync(ShellSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
