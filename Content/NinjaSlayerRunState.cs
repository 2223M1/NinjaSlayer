namespace NinjaSlayer.Content;

public sealed class NinjaSlayerRunState
{
    public bool PendingAncientEntranceAnimation { get; set; }

    public List<string> CompletedBossGreetingRoomKeys { get; set; } = [];
}

public static class NinjaSlayerRunStateNormalizer
{
    public static bool TryNormalizeRoomKeys(
        NinjaSlayerRunState state,
        out List<string> normalizedRoomKeys)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.CompletedBossGreetingRoomKeys is not { } roomKeys)
        {
            normalizedRoomKeys = [];
            return true;
        }

        normalizedRoomKeys = new(roomKeys.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? roomKey in roomKeys)
        {
            if (!string.IsNullOrEmpty(roomKey) && seen.Add(roomKey))
            {
                normalizedRoomKeys.Add(roomKey);
            }
        }

        return normalizedRoomKeys.Count != roomKeys.Count
            || !normalizedRoomKeys.SequenceEqual(roomKeys, StringComparer.Ordinal);
    }
}
