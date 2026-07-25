using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionSeekPrimerPolicyTests
{
    [Fact]
    public void ValidationAtOneFrameEnablesCorrection()
    {
        TimeSpan oneFrame = TimeSpan.FromSeconds(
            TransitionFrameDropClock.FrameDurationSeconds);

        Assert.True(TransitionSeekPrimerPolicy.CanEnableFrameCorrection(oneFrame));
    }

    [Fact]
    public void ValidationOverOneFrameDisablesCorrection()
    {
        TimeSpan overOneFrame = TimeSpan.FromSeconds(
            TransitionFrameDropClock.FrameDurationSeconds + 0.001);

        Assert.False(TransitionSeekPrimerPolicy.CanEnableFrameCorrection(overOneFrame));
    }

    [Fact]
    public void NegativeDurationFailsClosed()
    {
        Assert.False(
            TransitionSeekPrimerPolicy.CanEnableFrameCorrection(TimeSpan.FromMilliseconds(-1)));
    }
}
