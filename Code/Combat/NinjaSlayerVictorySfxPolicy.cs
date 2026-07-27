namespace NinjaSlayer.Code.Combat;

public static class NinjaSlayerVictorySfxPolicy
{
    public static bool ShouldSuppress(string streamName, bool partyContainsNinjaSlayer) =>
        partyContainsNinjaSlayer
        && string.Equals(streamName, "victory.mp3", StringComparison.Ordinal);
}
