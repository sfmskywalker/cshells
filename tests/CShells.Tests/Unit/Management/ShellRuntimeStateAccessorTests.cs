using CShells.Features;
using CShells.Hosting;
using CShells.Management;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.Unit.Management;

public class ShellRuntimeStateAccessorTests
{
    [Fact(DisplayName = "GetShell reports active last-known-good runtime with desired drift and blocking reason")]
    public void GetShell_LastKnownGoodAppliedRuntime_RemainsActiveWhileDesiredDrifts()
    {
        // Arrange
        var shellId = new ShellId("Contoso");
        var store = new ShellRuntimeStateStore();
        var accessor = new ShellRuntimeStateAccessor(store);
        var appliedSettings = new ShellSettings(shellId, ["Core"]);
        var desiredSettings = new ShellSettings(shellId, ["Core", "MissingFeature"]);
        var snapshot = new RuntimeFeatureCatalogSnapshot(
            1,
            Array.Empty<System.Reflection.Assembly>(),
            Array.Empty<ShellFeatureDescriptor>(),
            new Dictionary<string, ShellFeatureDescriptor>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
        var appliedContext = new ShellContext(appliedSettings, new ServiceCollection().BuildServiceProvider(), ["Core"]);

        store.RecordDesired(appliedSettings);
        store.CommitAppliedRuntime(shellId, appliedSettings, snapshot, appliedContext);
        store.RecordDesired(desiredSettings);
        store.MarkDeferred(shellId, ["MissingFeature"], "Feature 'MissingFeature' is not available in the current runtime feature catalog.");

        // Act
        var status = accessor.GetShell(shellId);

        // Assert
        Assert.NotNull(status);
        Assert.Equal(2, status!.DesiredGeneration);
        Assert.Equal(1, status.AppliedGeneration);
        Assert.Equal(ShellReconciliationOutcome.Active, status.Outcome);
        Assert.False(status.IsInSync);
        Assert.True(status.IsRoutable);
        Assert.Equal("Feature 'MissingFeature' is not available in the current runtime feature catalog.", status.BlockingReason);
        Assert.Equal(["MissingFeature"], status.MissingFeatures);
    }

    [Fact(DisplayName = "GetAllShells includes configured shells with no applied runtime")]
    public void GetAllShells_IncludesDesiredOnlyShells()
    {
        // Arrange
        var store = new ShellRuntimeStateStore();
        var accessor = new ShellRuntimeStateAccessor(store);
        store.RecordDesired(new ShellSettings(new ShellId("Deferred"), ["MissingFeature"]));
        store.MarkDeferred(new ShellId("Deferred"), ["MissingFeature"], "Missing required feature.");

        // Act
        var statuses = accessor.GetAllShells();

        // Assert
        var status = Assert.Single(statuses);
        Assert.Equal(new ShellId("Deferred"), status.ShellId);
        Assert.Null(status.AppliedGeneration);
        Assert.Equal(ShellReconciliationOutcome.DeferredDueToMissingFeatures, status.Outcome);
        Assert.False(status.IsRoutable);
        Assert.False(status.IsInSync);
    }
}

