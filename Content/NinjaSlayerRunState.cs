namespace NinjaSlayer.Content;

public sealed class NinjaSlayerRunState
{
    public bool EventValidationEnabled { get; set; }

    public bool PendingAncientEntranceAnimation { get; set; }

    public List<string> CompletedBossGreetingRoomKeys { get; set; } = [];
}
