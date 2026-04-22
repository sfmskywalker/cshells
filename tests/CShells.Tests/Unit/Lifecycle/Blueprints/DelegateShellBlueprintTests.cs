using CShells.Lifecycle.Blueprints;

namespace CShells.Tests.Unit.Lifecycle.Blueprints;

public class DelegateShellBlueprintTests
{
    [Fact(DisplayName = "ComposeAsync invokes the delegate against a fresh ShellBuilder each call")]
    public async Task ComposeAsync_InvokesDelegate_OnEachCall()
    {
        var invocations = 0;
        var bp = new DelegateShellBlueprint("payments", b =>
        {
            invocations++;
            b.WithFeatures("Core");
        });

        var s1 = await bp.ComposeAsync();
        var s2 = await bp.ComposeAsync();

        Assert.Equal(2, invocations);
        Assert.Equal("payments", s1.Id.Name);
        Assert.Equal("payments", s2.Id.Name);
        Assert.NotSame(s1, s2);
        Assert.Contains("Core", s1.EnabledFeatures);
        Assert.Contains("Core", s2.EnabledFeatures);
    }

    [Fact(DisplayName = "Metadata defaults to empty and flows from constructor")]
    public void Metadata_FlowsFromConstructor()
    {
        var defaults = new DelegateShellBlueprint("a", _ => { });
        Assert.Empty(defaults.Metadata);

        var withMeta = new DelegateShellBlueprint("b", _ => { }, new Dictionary<string, string> { ["owner"] = "team-x" });
        Assert.Equal("team-x", withMeta.Metadata["owner"]);
    }

    [Fact(DisplayName = "Delegate capture reflects updates between composes")]
    public async Task Delegate_Capture_RefreshesBetweenComposes()
    {
        var feature = "Core";
        var bp = new DelegateShellBlueprint("payments", b => b.WithFeatures(feature));

        var s1 = await bp.ComposeAsync();
        feature = "Analytics";
        var s2 = await bp.ComposeAsync();

        Assert.Equal(["Core"], s1.EnabledFeatures);
        Assert.Equal(["Analytics"], s2.EnabledFeatures);
    }
}
