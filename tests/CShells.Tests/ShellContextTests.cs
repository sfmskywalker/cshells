namespace CShells.Tests;

public class ShellContextTests
{
    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = new TestServiceProvider();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ShellContext(null!, serviceProvider));
        Assert.Equal("settings", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var settings = new ShellSettings(new("Test"));

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ShellContext(settings, null!));
        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_SetsProperties()
    {
        // Arrange
        var settings = new ShellSettings(new("TestShell"));
        var serviceProvider = new TestServiceProvider();

        // Act
        var context = new ShellContext(settings, serviceProvider);

        // Assert
        Assert.Same(settings, context.Settings);
        Assert.Same(serviceProvider, context.ServiceProvider);
    }

    [Fact]
    public void Id_ReturnsShellIdFromSettings()
    {
        // Arrange
        var settings = new ShellSettings(new("MyShell"));
        var serviceProvider = new TestServiceProvider();

        // Act
        var context = new ShellContext(settings, serviceProvider);

        // Assert
        Assert.Equal("MyShell", context.Id.Name);
        Assert.Equal(settings.Id, context.Id);
    }

    private class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
