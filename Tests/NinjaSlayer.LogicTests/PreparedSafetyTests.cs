using NinjaSlayer.Code.Prepared;

namespace NinjaSlayer.LogicTests;

public sealed class PreparedSafetyTests
{
    [Fact]
    public void PileChangeCleanupRequiresAConfirmedDrawPileExitInTheCurrentCombat()
    {
        Assert.True(PreparedSafetyPolicy.ShouldClearAfterPileChange(
            isPrepared: true,
            oldPileWasDraw: true,
            remainsInDrawPile: false,
            belongsToCurrentCombat: true));

        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(true, true, true, true));
        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(true, true, false, false));
        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(true, false, false, true));
        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(false, true, false, true));
    }

    [Fact]
    public void LifecycleCleanupPreservesOnlyValidPreparedCardsInTheDrawPile()
    {
        Assert.False(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, true, true, false));
        Assert.True(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, false, true, false));
        Assert.True(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, true, false, false));
        Assert.True(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, true, true, true));
        Assert.False(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(false, false, false, true));
    }
}
