using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class FacingScaleMathTests
{
    [Theory]
    [InlineData(0.5f, false, 0.5f)]
    [InlineData(-0.5f, false, 0.5f)]
    [InlineData(0.5f, true, -0.5f)]
    [InlineData(-0.5f, true, -0.5f)]
    public void WithFacingPreservesMagnitude(float input, bool faceLeft, float expected)
    {
        Assert.Equal(expected, FacingScaleMath.WithFacing(input, faceLeft));
    }

    [Fact]
    public void WithFacingUsesFallbackForCollapsedScale()
    {
        Assert.Equal(-0.75f, FacingScaleMath.WithFacing(0f, faceLeft: true, 0.75f));
    }

    [Theory]
    [InlineData(-1f, false, true)]
    [InlineData(1f, true, false)]
    [InlineData(0f, true, true)]
    public void IsFacingLeftUsesFallbackOnlyForCollapsedScale(
        float input,
        bool fallback,
        bool expected)
    {
        Assert.Equal(expected, FacingScaleMath.IsFacingLeft(input, fallback));
    }
}
