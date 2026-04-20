using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Management;

public class ShellRuntimeStateAccessorTests
{
    [Fact(DisplayName = "GetShell reports active runtime with missing features when committed with partial feature set")]
    public void GetShell_CommittedWithMissingFeatures_ReportsActiveWithMissingFeatures()
    {
        // Arrange
        var shellId = new ShellId("Contoso");
        var store = new ShellRuntimeStateStore();
        var accessor = new ShellRuntimeStateAccessor(store);
        var settings = new ShellSettings(shellId, ["Core", "MissingFeature"]);
        var snapshot = new RuntimeFeatureCatalogSnapshot(
            1,
            Array.Empty<System.Reflection.Assembly>(),
            Array.Empty<ShellFeatureDescriptor>(),
            new Dictionary<string, ShellFeatureDescriptor>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
        var appliedContext = new ShellContext(settings, new ServiceCollection().BuildServiceProvider(), ["Core"], ["MissingFeature"]);

        store.RecordDesired(settings);
        store.CommitAppliedRuntime(shellId, settings, snapshot, appliedContext, ["MissingFeature"]);

        // Act
        var status = accessor.GetShell(shellId);

        // Assert
        Assert.NotNull(status);
        Assert.Equal(1, status!.DesiredGeneration);
        Assert.Equal(1, status.AppliedGeneration);
        Assert.Equal(ShellReconciliationOutcome.ActiveWithMissingFeatures, status.Outcome);
        Assert.True(status.IsInSync);
        Assert.True(status.IsRoutable);
        Assert.Null(status.BlockingReason);
        Assert.Equal(["MissingFeature"], status.MissingFeatures);
    }

    [Fact(DisplayName = "GetAllShells includes shells activated with missing features as routable")]
    public void GetAllShells_ShellWithMissingFeatures_IsRoutable()
    {
        // Arrange
        var store = new ShellRuntimeStateStore();
        var accessor = new ShellRuntimeStateAccessor(store);
        var shellId = new ShellId("Partial");
        var settings = new ShellSettings(shellId, ["MissingFeature"]);
        var snapshot = new RuntimeFeatureCatalogSnapshot(
            1,
            Array.Empty<System.Reflection.Assembly>(),
            Array.Empty<ShellFeatureDescriptor>(),
            new Dictionary<string, ShellFeatureDescriptor>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
        var appliedContext = new ShellContext(settings, new ServiceCollection().BuildServiceProvider(), [], ["MissingFeature"]);

        store.RecordDesired(settings);
        store.CommitAppliedRuntime(shellId, settings, snapshot, appliedContext, ["MissingFeature"]);

        // Act
        var statuses = accessor.GetAllShells();

        // Assert
        var status = Assert.Single(statuses);
        Assert.Equal(new("Partial"), status.ShellId);
        Assert.Equal(1, status.AppliedGeneration);
        Assert.Equal(ShellReconciliationOutcome.ActiveWithMissingFeatures, status.Outcome);
        Assert.True(status.IsRoutable);
        Assert.True(status.IsInSync);
        Assert.Equal(["MissingFeature"], status.MissingFeatures);
    }
}
