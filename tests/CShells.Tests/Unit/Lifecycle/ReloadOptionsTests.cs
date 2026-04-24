using CShells.Lifecycle;

namespace CShells.Tests.Unit.Lifecycle;

public class ReloadOptionsTests
{
    [Fact(DisplayName = "Default MaxDegreeOfParallelism is 8")]
    public void Default_Is8()
    {
        var opts = new ReloadOptions();
        Assert.Equal(8, opts.MaxDegreeOfParallelism);
    }

    [Theory(DisplayName = "EnsureValid accepts in-range parallelism")]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(64)]
    public void Valid_InRange(int value)
    {
        var opts = new ReloadOptions(value);
        opts.EnsureValid();
    }

    [Theory(DisplayName = "EnsureValid throws for out-of-range parallelism")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65)]
    [InlineData(int.MaxValue)]
    public void Invalid_OutOfRange_Throws(int value)
    {
        var opts = new ReloadOptions(value);
        Assert.Throws<ArgumentOutOfRangeException>(() => opts.EnsureValid());
    }
}
