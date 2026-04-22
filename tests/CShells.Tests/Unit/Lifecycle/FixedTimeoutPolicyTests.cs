using CShells.Lifecycle;
using CShells.Lifecycle.Policies;

namespace CShells.Tests.Unit.Lifecycle;

public class FixedTimeoutPolicyTests
{
    [Fact(DisplayName = "InitialTimeout reflects the constructor value")]
    public void InitialTimeout_Reflects_Ctor()
    {
        var p = new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(42));
        Assert.Equal(TimeSpan.FromSeconds(42), p.InitialTimeout);
        Assert.False(p.IsUnbounded);
    }

    [Fact(DisplayName = "TryExtend always returns false and grants zero")]
    public void TryExtend_AlwaysFalse()
    {
        var p = new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(1));
        Assert.False(p.TryExtend(TimeSpan.FromSeconds(10), out var granted));
        Assert.Equal(TimeSpan.Zero, granted);
    }

    [Fact(DisplayName = "Zero or negative timeout throws")]
    public void InvalidTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimeoutDrainPolicy(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimeoutDrainPolicy(TimeSpan.FromSeconds(-1)));
    }

    [Fact(DisplayName = "Default ctor yields 30-second timeout")]
    public void DefaultCtor_Thirty_Seconds()
    {
        var p = new FixedTimeoutDrainPolicy();
        Assert.Equal(TimeSpan.FromSeconds(30), p.InitialTimeout);
    }
}
