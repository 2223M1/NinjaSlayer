using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class YamotoKokiRelicPolicyTests
{
    [Fact]
    public void FifthCombatRemainsActiveUntilCombatCompletion()
    {
        Assert.True(YamotoKokiRelicLifetimePolicy.IsActive(1, isMelted: false));

        int remaining = YamotoKokiRelicLifetimePolicy.CompleteCombat(1);

        Assert.Equal(0, remaining);
        Assert.False(YamotoKokiRelicLifetimePolicy.IsActive(remaining, isMelted: false));
    }

    [Fact]
    public void MeltedAndExhaustedRelicsAreNotActive()
    {
        Assert.False(YamotoKokiRelicLifetimePolicy.IsActive(3, isMelted: true));
        Assert.False(YamotoKokiRelicLifetimePolicy.IsActive(0, isMelted: false));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    public void FarewellRequiresTheLastActiveRelicAndRunsOnce(
        int activeRelicCount,
        bool farewellAlreadyPlayed,
        bool expected)
    {
        Assert.Equal(
            expected,
            YamotoKokiRelicLifetimePolicy.ShouldPlayFarewell(
                activeRelicCount,
                farewellAlreadyPlayed));
    }

    [Theory]
    [InlineData(true, 1, false, false, true)]
    [InlineData(true, 0, true, false, false)]
    [InlineData(true, 1, true, false, false)]
    [InlineData(true, 1, false, true, false)]
    [InlineData(false, 1, false, false, false)]
    public void FinishedRoomRestoreIsFailClosed(
        bool isFinishedCombat,
        int activeRelicCount,
        bool farewellPlayed,
        bool companionPresent,
        bool expected)
    {
        Assert.Equal(
            expected,
            YamotoKokiCompanionRestorePolicy.ShouldRestore(
                isFinishedCombat,
                activeRelicCount,
                farewellPlayed,
                companionPresent));
    }
}
