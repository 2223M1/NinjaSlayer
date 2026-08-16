using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class CompanionDamageMathTests
{
    [Theory]
    [InlineData(15, 1, 15)]
    [InlineData(15, 2, 30)]
    [InlineData(15, 3, 45)]
    [InlineData(15, 4, 60)]
    [InlineData(6, 1, 6)]
    [InlineData(6, 2, 12)]
    [InlineData(6, 3, 18)]
    [InlineData(6, 4, 24)]
    public void DamageScalesWithActiveRelicCount(
        int baseDamage,
        int activeRelicCount,
        int expected)
    {
        Assert.Equal(
            expected,
            CompanionDamageMath.ScaleForActiveRelics(baseDamage, activeRelicCount));
    }
}
