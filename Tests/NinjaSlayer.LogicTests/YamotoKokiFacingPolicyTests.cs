using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class YamotoKokiFacingPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    public void ResolveCompanionFacingOnlyReversesWhenEnemiesOccupyBothSides(
        bool ownerFacesLeft,
        bool hasEnemyOnLeft,
        bool hasEnemyOnRight,
        bool expected)
    {
        Assert.Equal(
            expected,
            YamotoKokiFacingPolicy.ResolveCompanionFacing(
                ownerFacesLeft,
                hasEnemyOnLeft,
                hasEnemyOnRight));
    }
}
