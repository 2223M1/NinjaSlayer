namespace NinjaSlayer.Code.Combat;

public static class YamotoKokiRelicLifetimePolicy
{
    public static bool IsActive(int combatsLeft, bool isMelted) =>
        combatsLeft > 0 && !isMelted;

    public static int CompleteCombat(int combatsLeft) =>
        Math.Max(0, combatsLeft - 1);

    public static bool ShouldPlayFarewell(int activeRelicCount, bool farewellAlreadyPlayed) =>
        activeRelicCount == 0 && !farewellAlreadyPlayed;
}
