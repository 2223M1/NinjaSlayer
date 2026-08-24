using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class RapidAttackTrajectoryTests
{
    [Fact]
    public void ReturnBudgetAddsOnlyUnconsumedAnimationTime()
    {
        Assert.Equal(0.35f, RapidAttackTrajectory.AddReturnSeconds(0.15f, 0.2f), precision: 5);
        Assert.Equal(0.15f, RapidAttackTrajectory.RemainingReturnSeconds(0.2f, 0.25f));
        Assert.Equal(0f, RapidAttackTrajectory.RemainingReturnSeconds(0.2f, 1.5f));
    }

    [Fact]
    public void ReturnBudgetAccumulatesThreeInterruptedCards()
    {
        float pending = RapidAttackTrajectory.RemainingReturnSeconds(0.2f, 0.5f);
        pending = RapidAttackTrajectory.AddReturnSeconds(pending, 0.2f);
        pending = RapidAttackTrajectory.RemainingReturnSeconds(pending, 0.25f);
        pending = RapidAttackTrajectory.AddReturnSeconds(pending, 0.2f);

        Assert.Equal(0.425f, pending, precision: 5);
    }

    [Fact]
    public void ContinuationRetreatsHalfwayBeforeReturningToPeak()
    {
        static float Linear(float progress) => progress;

        Assert.Equal(120f, RapidAttackTrajectory.GetContinuationOffset(0f, 120f, 60f, 120f, Linear));
        Assert.Equal(60f, RapidAttackTrajectory.GetContinuationOffset(0.5f, 120f, 60f, 120f, Linear));
        Assert.Equal(120f, RapidAttackTrajectory.GetContinuationOffset(1f, 120f, 60f, 120f, Linear));
    }
}
