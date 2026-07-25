using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class YamotoKokiOrbitMathTests
{
    [Fact]
    public void TwoBombsOccupyOppositeSidesOfTheOverheadArc()
    {
        (float leftX, float leftY) = YamotoKokiOrbitMath.GetOffset(2, 0);
        (float rightX, float rightY) = YamotoKokiOrbitMath.GetOffset(2, 1);

        Assert.True(leftX > 0f);
        Assert.True(rightX < 0f);
        Assert.True(leftY < 0f);
        Assert.True(rightY < 0f);
    }

    [Fact]
    public void AdditionalBombsContinueOnLargerUnboundedLayers()
    {
        (float firstLayerX, float firstLayerY) = YamotoKokiOrbitMath.GetOffset(7, 0);
        (float secondLayerX, float secondLayerY) = YamotoKokiOrbitMath.GetOffset(7, 6);
        float firstRadius = MathF.Sqrt(firstLayerX * firstLayerX + firstLayerY * firstLayerY);
        float secondRadius = MathF.Sqrt(secondLayerX * secondLayerX + secondLayerY * secondLayerY);

        Assert.True(secondRadius > firstRadius);
        Assert.True(secondLayerY < 0f);
    }

    [Fact]
    public void InvalidIndicesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => YamotoKokiOrbitMath.GetOffset(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => YamotoKokiOrbitMath.GetOffset(2, 2));
    }
}
