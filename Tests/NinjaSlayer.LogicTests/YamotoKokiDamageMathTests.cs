using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class YamotoKokiDamageMathTests
{
    [Theory]
    [InlineData(6, 1, 6)]
    [InlineData(6, 2, 12)]
    [InlineData(6, 4, 24)]
    [InlineData(4, 1, 4)]
    [InlineData(4, 3, 12)]
    [InlineData(4, 4, 16)]
    public void DamageScalesWithPartySize(int baseDamage, int playerCount, int expected)
    {
        Assert.Equal(expected, YamotoKokiDamageMath.ScaleForParty(baseDamage, playerCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MissingPartyCountFallsBackToSingleplayer(int playerCount)
    {
        Assert.Equal(4, YamotoKokiDamageMath.ScaleForParty(4, playerCount));
    }
}
