using CShells.Lifecycle;
using CShells.Lifecycle.Policies;

namespace CShells.Tests.Unit.Lifecycle;

public class ExtensibleTimeoutPolicyTests
{
    [Fact(DisplayName = "Grants extensions up to the cap")]
    public void Grants_UpTo_Cap()
    {
        var p = new ExtensibleTimeoutDrainPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        Assert.True(p.TryExtend(TimeSpan.FromSeconds(5), out var g1));
        Assert.Equal(TimeSpan.FromSeconds(5), g1);
        Assert.True(p.TryExtend(TimeSpan.FromSeconds(5), out var g2));
        Assert.Equal(TimeSpan.FromSeconds(5), g2);

        // Total extensions so far: 10s. Cap = 30s, initial = 10s → 20s of extensions allowed.
        // Next request for 15s gets partial 10s (remaining headroom).
        Assert.True(p.TryExtend(TimeSpan.FromSeconds(15), out var g3));
        Assert.Equal(TimeSpan.FromSeconds(10), g3);

        // Cap reached — further requests rejected.
        Assert.False(p.TryExtend(TimeSpan.FromSeconds(1), out var g4));
        Assert.Equal(TimeSpan.Zero, g4);
    }

    [Fact(DisplayName = "Partial grant when cap only has headroom left")]
    public void Partial_Grant_AtCap()
    {
        var p = new ExtensibleTimeoutDrainPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
        Assert.True(p.TryExtend(TimeSpan.FromSeconds(10), out var granted));
        Assert.Equal(TimeSpan.FromSeconds(5), granted); // cap-initial = 5s headroom
    }

    [Fact(DisplayName = "Cap less than initial throws")]
    public void InvalidCap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExtensibleTimeoutDrainPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
    }
}
