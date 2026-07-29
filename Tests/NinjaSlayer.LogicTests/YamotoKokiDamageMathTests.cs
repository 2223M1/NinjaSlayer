using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class YamotoKokiDamageMathTests
{
    [Theory]
    [InlineData(6, 1, 6)]
    [InlineData(6, 2, 12)]
    [InlineData(6, 3, 18)]
    [InlineData(6, 4, 24)]
    [InlineData(4, 1, 4)]
    [InlineData(4, 2, 8)]
    [InlineData(4, 3, 12)]
    [InlineData(4, 4, 16)]
    public void DamageScalesWithActiveRelicCount(
        int baseDamage,
        int activeRelicCount,
        int expected)
    {
        Assert.Equal(
            expected,
            YamotoKokiDamageMath.ScaleForActiveRelics(baseDamage, activeRelicCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MissingRelicsContributeNoDamage(int activeRelicCount)
    {
        Assert.Equal(
            0,
            YamotoKokiDamageMath.ScaleForActiveRelics(4, activeRelicCount));
    }
}
