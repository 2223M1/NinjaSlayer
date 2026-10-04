using NinjaSlayer.Code.Combat;
using Xunit;

namespace NinjaSlayer.LogicTests;

public sealed class TornadoSpinTimingTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 0, 0)]
    [InlineData(5, 0, 1)]
    [InlineData(7, 0, 1)]
    [InlineData(8, 0, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(11, 0, 1)]
    [InlineData(4, 1, 0)]
    [InlineData(8, 1, 0)]
    [InlineData(9, 1, 1)]
    [InlineData(14, 1, 1)]
    [InlineData(15, 1, 1)]
    [InlineData(20, 1, 1)]
    [InlineData(21, 1, 1)]
    [InlineData(100, 1, 1)]
    [InlineData(100, 2, 0)]
    public void SpinEligibilityUsesCurrentActionDuration(int hits, int speed, int expected)
    {
        float duration = TornadoSpinTiming.SecondsToFinalHit(hits, (CombatActionSpeed)speed);
        Assert.Equal((TornadoAudioMode)expected, TornadoSpinTiming.Select(hits, duration, false));
    }

    [Fact]
    public void IntroMustFitBeforeFinalHitButOutroMayOutlastAnimation()
    {
        Assert.Equal(TornadoAudioMode.PerHit, TornadoSpinTiming.Select(4, TornadoSpinTiming.IntroSeconds - 0.001f, false));
        Assert.Equal(TornadoAudioMode.Spin, TornadoSpinTiming.Select(4, TornadoSpinTiming.IntroSeconds, false));
        Assert.Equal(TornadoAudioMode.Spin, TornadoSpinTiming.Select(4, 10f, false));
        Assert.Equal(TornadoAudioMode.PerHit, TornadoSpinTiming.Select(3, 20f, false));
        Assert.Equal(TornadoAudioMode.PerHit, TornadoSpinTiming.Select(40, 20f, true));
    }

    [Fact]
    public void FinalHitDeadlineExcludesReturnAndPostHitRecovery()
    {
        Assert.Equal(1.2f, TornadoSpinTiming.SecondsToFinalHit(4, CombatActionSpeed.Normal), 5);
        Assert.Equal(0.6f, TornadoSpinTiming.SecondsToFinalHit(4, CombatActionSpeed.Fast), 5);
        Assert.Equal(1.55f, TornadoSpinTiming.SecondsToFinalHit(5, CombatActionSpeed.Normal), 5);
        Assert.Equal(1.475f, TornadoSpinTiming.SecondsToFinalHit(9, CombatActionSpeed.Fast), 5);
        Assert.Equal(0f, TornadoSpinTiming.SecondsToFinalHit(0, CombatActionSpeed.Normal));
    }
}
