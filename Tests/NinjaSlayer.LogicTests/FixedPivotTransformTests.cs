using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.LogicTests;

public sealed class FixedPivotTransformTests
{
    [Theory]
    [InlineData(0f, 1f, 1f, 8f, 17f)]
    [InlineData(90f, 1f, 1f, 13f, 18f)]
    [InlineData(180f, 1f, 1f, 12f, 23f)]
    [InlineData(270f, 1f, 1f, 7f, 22f)]
    [InlineData(360f, 1f, 1f, 8f, 17f)]
    [InlineData(90f, 2f, 0.5f, 11.5f, 16f)]
    [InlineData(180f, -1f, 1f, 8f, 23f)]
    public void BodyPositionKeepsMarkerOnFixedPivot(
        float rotationDegrees,
        float scaleX,
        float scaleY,
        float expectedX,
        float expectedY)
    {
        (float x, float y) = FixedPivotMath.ResolveBodyPosition(
            10f,
            20f,
            2f,
            3f,
            rotationDegrees,
            scaleX,
            scaleY);

        Assert.Equal(expectedX, x, precision: 4);
        Assert.Equal(expectedY, y, precision: 4);
    }
}
