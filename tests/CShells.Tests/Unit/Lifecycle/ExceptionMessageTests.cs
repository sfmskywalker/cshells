using CShells.Lifecycle;

namespace CShells.Tests.Unit.Lifecycle;

public class ExceptionMessageTests
{
    [Fact(DisplayName = "BlueprintNotMutableException without SourceId mentions the name")]
    public void NotMutable_NoSourceId_MentionsName()
    {
        var ex = new BlueprintNotMutableException("payments");
        Assert.Equal("payments", ex.Name);
        Assert.Null(ex.SourceId);
        Assert.Contains("payments", ex.Message);
        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "BlueprintNotMutableException with SourceId mentions both")]
    public void NotMutable_WithSourceId_MentionsBoth()
    {
        var ex = new BlueprintNotMutableException("payments", "Configuration");
        Assert.Equal("payments", ex.Name);
        Assert.Equal("Configuration", ex.SourceId);
        Assert.Contains("payments", ex.Message);
        Assert.Contains("Configuration", ex.Message);
    }

    [Fact(DisplayName = "ShellBlueprintUnavailableException wraps inner cause")]
    public void Unavailable_WrapsInner()
    {
        var inner = new InvalidOperationException("db down");
        var ex = new ShellBlueprintUnavailableException("acme", inner);
        Assert.Equal("acme", ex.Name);
        Assert.Same(inner, ex.InnerException);
    }
}
