using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class RapidAttackTrajectoryTests
{
    [Fact]
    public void ContinuationRetreatsHalfwayBeforeReturningToPeak()
    {
        static float Linear(float progress) => progress;

        Assert.Equal(120f, RapidAttackTrajectory.GetContinuationOffset(0f, 120f, 60f, 120f, Linear));
        Assert.Equal(60f, RapidAttackTrajectory.GetContinuationOffset(0.5f, 120f, 60f, 120f, Linear));
        Assert.Equal(120f, RapidAttackTrajectory.GetContinuationOffset(1f, 120f, 60f, 120f, Linear));
    }
}
