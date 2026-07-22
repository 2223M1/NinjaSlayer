namespace NinjaSlayer.Code.Compatibility;

public static class NancyAvailabilityPolicy
{
    public static bool ShouldFilterCandidates(bool hasCurrentRun, bool runHasNinjaSlayer) =>
        hasCurrentRun && !runHasNinjaSlayer;

    public static bool ShouldRepairLoadedSelection(
        bool isGlory,
        bool runHasNinjaSlayer,
        bool hasAncient,
        bool selectedNancy) =>
        isGlory && !runHasNinjaSlayer && hasAncient && selectedNancy;
}
