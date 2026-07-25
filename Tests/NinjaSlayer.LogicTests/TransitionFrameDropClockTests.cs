using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionFrameDropClockTests
{
    private const double Frame = TransitionFrameDropClock.FrameDurationSeconds;

    [Fact]
    public void DoesNotSeekWhenPlaybackIsLessThanOneFrameBehind()
    {
        var clock = new TransitionFrameDropClock(2.0);

        TransitionFrameDropDecision decision = clock.Evaluate(
            wallElapsedSeconds: 0.5,
            streamPositionSeconds: 0.5 - Frame + 0.001);

        Assert.False(decision.ShouldSeek);
    }

    [Fact]
    public void SeeksForwardToTheCurrentQuantizedFrame()
    {
        var clock = new TransitionFrameDropClock(2.0);

        TransitionFrameDropDecision decision = clock.Evaluate(
            wallElapsedSeconds: 0.421,
            streamPositionSeconds: 0.237);

        Assert.True(decision.ShouldSeek);
        Assert.Equal(10.0 / 24.0, decision.TargetPositionSeconds, precision: 9);
        Assert.Equal(4, decision.SkippedFrames);
        Assert.True(decision.TargetPositionSeconds > 0.237);
    }

    [Fact]
    public void EnforcesTwoFrameCooldownBetweenSeekRequests()
    {
        var clock = new TransitionFrameDropClock(2.0);
        Assert.True(clock.Evaluate(0.5, 0.25).ShouldSeek);

        Assert.False(clock.Evaluate(0.5 + Frame, 0.25).ShouldSeek);
        Assert.True(clock.Evaluate(0.5 + Frame * 2.0, 0.25).ShouldSeek);
    }

    [Fact]
    public void ClampsSeekTargetToTheLastVideoFrame()
    {
        var clock = new TransitionFrameDropClock(2.0);

        TransitionFrameDropDecision decision = clock.Evaluate(5.0, 1.0);

        Assert.True(decision.ShouldSeek);
        Assert.Equal(47.0 / 24.0, decision.TargetPositionSeconds, precision: 9);
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.5, double.PositiveInfinity)]
    [InlineData(-0.1, 0.0)]
    [InlineData(0.5, -0.1)]
    public void RejectsInvalidClockOrStreamPositions(double wallElapsed, double streamPosition)
    {
        var clock = new TransitionFrameDropClock(2.0);

        Assert.False(clock.Evaluate(wallElapsed, streamPosition).ShouldSeek);
    }

    [Fact]
    public void NeverSeeksBackwardWhenTheStreamIsAhead()
    {
        var clock = new TransitionFrameDropClock(2.0);

        Assert.False(clock.Evaluate(0.5, 0.75).ShouldSeek);
    }

    [Fact]
    public void ANewPlaybackClockDoesNotRetainThePreviousCooldown()
    {
        var first = new TransitionFrameDropClock(2.0);
        Assert.True(first.Evaluate(0.5, 0.25).ShouldSeek);

        var second = new TransitionFrameDropClock(2.0);
        Assert.True(second.Evaluate(0.5, 0.25).ShouldSeek);
    }

    [Fact]
    public void WallClockEndsPlaybackAtTheRequestedDuration()
    {
        var clock = new TransitionFrameDropClock(2.0);

        Assert.False(clock.HasEnded(1.999));
        Assert.True(clock.HasEnded(2.0));
        Assert.False(clock.HasEnded(double.NaN));
    }
}
