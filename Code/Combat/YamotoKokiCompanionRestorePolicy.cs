namespace NinjaSlayer.Code.Combat;

public static class YamotoKokiCompanionRestorePolicy
{
    public static bool ShouldRestore(
        bool isFinishedCombat,
        int activeRelicCount,
        bool farewellPlayed,
        bool companionPresent) =>
        isFinishedCombat
        && activeRelicCount > 0
        && !farewellPlayed
        && !companionPresent;
}
