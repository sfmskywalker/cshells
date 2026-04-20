using CShells.Features;
using CShells.Features.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Features;

public class FeatureConfigurationBinderTests
{
    [Fact(DisplayName = "BindAndConfigure binds interface-typed collection properties")]
    public void BindAndConfigure_WithICollectionProperty_BindsValues()
    {
        // Arrange
        var configuration = BuildConfiguration(new()
        {
            ["DefaultAdminUser:AdminRolePermissions:0"] = "ReadUsers",
            ["DefaultAdminUser:AdminRolePermissions:1"] = "ManageUsers"
        });
        var binder = new FeatureConfigurationBinder(configuration, new NoOpFeatureConfigurationValidator());
        var feature = new AdminUserFeature();

        // Act
        binder.BindAndConfigure(feature, "DefaultAdminUser");

        // Assert
        Assert.Equal(["ReadUsers", "ManageUsers"], feature.AdminRolePermissions);
    }

    [Fact(DisplayName = "BindAndConfigure still binds regular complex properties")]
    public void BindAndConfigure_WithComplexProperty_BindsValues()
    {
        // Arrange
        var configuration = BuildConfiguration(new()
        {
            ["Complex:Settings:Mode"] = "Strict",
            ["Complex:Settings:MaxItems"] = "7"
        });
        var binder = new FeatureConfigurationBinder(configuration, new NoOpFeatureConfigurationValidator());
        var feature = new ComplexFeature();

        // Act
        binder.BindAndConfigure(feature, "Complex");

        // Assert
        Assert.NotNull(feature.Settings);
        Assert.Equal("Strict", feature.Settings.Mode);
        Assert.Equal(7, feature.Settings.MaxItems);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class NoOpFeatureConfigurationValidator : IFeatureConfigurationValidator
    {
        public void Validate(object target, string contextName)
        {
        }
    }

    private sealed class AdminUserFeature : IShellFeature
    {
        public ICollection<string> AdminRolePermissions { get; set; } = [];

        public void ConfigureServices(IServiceCollection services)
        {
        }
    }

    private sealed class ComplexFeature : IShellFeature
    {
        public FeatureSettings? Settings { get; set; }

        public void ConfigureServices(IServiceCollection services)
        {
        }
    }

    private sealed class FeatureSettings
    {
        public string? Mode { get; set; }

        public int MaxItems { get; set; }
    }
}


