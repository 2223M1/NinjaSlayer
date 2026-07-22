using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.LogicTests;

public sealed class NancyAvailabilityPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void CandidateFilteringRequiresAnActiveNonNinjaSlayerRun(
        bool hasCurrentRun,
        bool runHasNinjaSlayer,
        bool expected)
    {
        Assert.Equal(
            expected,
            NancyAvailabilityPolicy.ShouldFilterCandidates(hasCurrentRun, runHasNinjaSlayer));
    }

    [Theory]
    [InlineData(true, false, true, true, true)]
    [InlineData(false, false, true, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, false)]
    public void LoadedSelectionRepairRequiresNancyInGloryWithoutNinjaSlayer(
        bool isGlory,
        bool runHasNinjaSlayer,
        bool hasAncient,
        bool selectedNancy,
        bool expected)
    {
        Assert.Equal(
            expected,
            NancyAvailabilityPolicy.ShouldRepairLoadedSelection(
                isGlory,
                runHasNinjaSlayer,
                hasAncient,
                selectedNancy));
    }
}
