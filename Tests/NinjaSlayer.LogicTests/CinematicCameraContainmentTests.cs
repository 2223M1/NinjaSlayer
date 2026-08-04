using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class CinematicCameraContainmentTests
{
    private const float KinBaselineMinimumX = -169.41176f;
    private const float KinBaselineMaximumX = 2089.41176f;
    private const float KinBaselineScale = 0.85f;

    [Theory]
    [InlineData(1.275f, 1336.471f)]
    [InlineData(1.7f, 1524.706f)]
    public void KinCinematicsMakeTheRightViewportEdgeFlushWithTheBaselineScene(
        float targetScale,
        float expectedCenter)
    {
        float center = CinematicCameraContainment.ClampCenter(
            desiredCenter: 1800f,
            baselineMinimum: KinBaselineMinimumX,
            baselineMaximum: KinBaselineMaximumX,
            baselineScale: KinBaselineScale,
            targetScale);
        float halfViewport = CinematicCameraContainment.ResolveVisibleHalfExtent(
            KinBaselineMinimumX,
            KinBaselineMaximumX,
            KinBaselineScale,
            targetScale);

        Assert.Equal(expectedCenter, center, precision: 3);
        Assert.Equal(KinBaselineMaximumX, center + halfViewport, precision: 3);
    }
}
