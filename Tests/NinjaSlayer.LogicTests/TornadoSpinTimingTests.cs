using NinjaSlayer.Code.Combat;
using Xunit;

namespace NinjaSlayer.LogicTests;

public sealed class TornadoSpinTimingTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 0, 0)]
    [InlineData(7, 0, 0)]
    [InlineData(8, 0, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(11, 0, 1)]
    [InlineData(14, 1, 0)]
    [InlineData(15, 1, 1)]
    [InlineData(20, 1, 1)]
    [InlineData(21, 1, 1)]
    [InlineData(100, 1, 1)]
    [InlineData(100, 2, 0)]
    public void SpinEligibilityUsesCurrentActionDuration(int hits, int speed, int expected)
    {
        float duration = TornadoSpinTiming.RemainingSeconds(hits, (CombatActionSpeed)speed);
        Assert.Equal((TornadoAudioMode)expected, TornadoSpinTiming.Select(hits, duration, false));
    }

    [Fact]
    public void ThresholdIncludesCompleteIntroAndOutro()
    {
        Assert.Equal(TornadoAudioMode.PerHit, TornadoSpinTiming.Select(4, TornadoSpinTiming.ShortSeconds - 0.001f, false));
        Assert.Equal(TornadoAudioMode.Spin, TornadoSpinTiming.Select(4, TornadoSpinTiming.ShortSeconds, false));
        Assert.Equal(TornadoAudioMode.Spin, TornadoSpinTiming.Select(4, 10f, false));
        Assert.Equal(TornadoAudioMode.PerHit, TornadoSpinTiming.Select(3, 20f, false));
        Assert.Equal(TornadoAudioMode.PerHit, TornadoSpinTiming.Select(40, 20f, true));
    }

    [Fact]
    public void LastHitHasOnlyItsActualReturnNotAnExtraDamageWait()
    {
        Assert.Equal(1.3f, TornadoSpinTiming.RemainingSeconds(4, CombatActionSpeed.Normal), 5);
        Assert.Equal(0.7f, TornadoSpinTiming.RemainingSeconds(4, CombatActionSpeed.Fast), 5);
        Assert.Equal(0f, TornadoSpinTiming.RemainingSeconds(0, CombatActionSpeed.Normal));
    }
}
