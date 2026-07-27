using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class NinjaSlayerVictorySfxPolicyTests
{
    [Theory]
    [InlineData("victory.mp3", true, true)]
    [InlineData("victory.mp3", false, false)]
    [InlineData("Victory.mp3", true, false)]
    [InlineData("relic_get.mp3", true, false)]
    public void SuppressionMatchesOnlyTheVanillaVictoryCue(
        string streamName,
        bool partyContainsNinjaSlayer,
        bool expected)
    {
        Assert.Equal(
            expected,
            NinjaSlayerVictorySfxPolicy.ShouldSuppress(
                streamName,
                partyContainsNinjaSlayer));
    }
}
