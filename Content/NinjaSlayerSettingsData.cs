namespace NinjaSlayer.Content;

public sealed class NinjaSlayerSettingsData
{
    public bool ForceAllEventsOnce { get; set; } = true;
    public bool FreeControlEnabled { get; set; }
    public bool NarrationEnabled { get; set; } = true;
    public bool TelemetryNoticeShown { get; set; }
    public bool PublicReplayEnabled { get; set; }
}
