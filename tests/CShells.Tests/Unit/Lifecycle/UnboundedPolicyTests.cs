using CShells.Lifecycle;
using CShells.Lifecycle.Policies;

namespace CShells.Tests.Unit.Lifecycle;

public class UnboundedPolicyTests
{
    [Fact(DisplayName = "InitialTimeout is null and IsUnbounded is true")]
    public void InitialTimeout_IsNull()
    {
        var p = new UnboundedDrainPolicy();
        Assert.Null(p.InitialTimeout);
        Assert.True(p.IsUnbounded);
    }

    [Fact(DisplayName = "TryExtend always grants the full request")]
    public void TryExtend_GrantsFully()
    {
        var p = new UnboundedDrainPolicy();
        Assert.True(p.TryExtend(TimeSpan.FromHours(5), out var granted));
        Assert.Equal(TimeSpan.FromHours(5), granted);
    }
}
