using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.LogicTests;

public sealed class VerticalSpinMathTests
{
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(60f, 0.5f)]
    [InlineData(89f, 0.18f)]
    [InlineData(91f, -0.18f)]
    [InlineData(120f, -0.5f)]
    [InlineData(180f, -1f)]
    [InlineData(360f, 1f)]
    public void ScaleRatioUsesSignedCosineWithEdgeOnClamp(float degrees, float expected)
    {
        Assert.Equal(expected, VerticalSpinMath.GetScaleRatio(degrees), precision: 4);
    }

    [Fact]
    public void SharedAxisRemainsFixedWhileSubjectsExchangeSides()
    {
        const float axis = 5f;

        Assert.Equal(axis, VerticalSpinMath.ProjectCoordinate(axis, axis, -1f));
        Assert.Equal(10f, VerticalSpinMath.ProjectCoordinate(axis, 0f, -1f));
        Assert.Equal(0f, VerticalSpinMath.ProjectCoordinate(axis, 10f, -1f));
    }
}
