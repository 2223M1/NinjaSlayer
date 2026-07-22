namespace NinjaSlayer.Code.Prepared;

public static class PreparedSafetyPolicy
{
    public static bool ShouldClearAfterPileChange(
        bool isPrepared,
        bool oldPileWasDraw,
        bool remainsInDrawPile,
        bool belongsToCurrentCombat) =>
        isPrepared && oldPileWasDraw && !remainsInDrawPile && belongsToCurrentCombat;

    public static bool ShouldClearAtLifecycleBoundary(
        bool isPrepared,
        bool remainsInDrawPile,
        bool belongsToCurrentCombat,
        bool combatIsEnding) =>
        isPrepared && (combatIsEnding || !remainsInDrawPile || !belongsToCurrentCombat);
}
