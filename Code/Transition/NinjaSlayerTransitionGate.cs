using System.Threading.Tasks;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerTransitionGate
{
    internal static bool Pending { get; set; }

    /// <summary>
    /// Seconds to wait before FadeOut returns and the caller begins run/save loading.
    /// Set by embark/load entry patches and consumed once in the FadeOut patch.
    /// </summary>
    internal static float LoadStartDelaySeconds { get; set; }

    /// <summary>
    /// The currently-playing transition video, started by the FadeOut patch and
    /// awaited by the reveal (RoomFadeIn/FadeIn) patches so asset loading overlaps the
    /// animation instead of producing a black hold afterwards.
    /// </summary>
    internal static Task? AnimationTask { get; set; }

    internal static float ConsumeLoadStartDelay()
    {
        var delay = LoadStartDelaySeconds;
        LoadStartDelaySeconds = 0f;
        return delay;
    }

    internal static void CancelPendingRequest()
    {
        Pending = false;
        LoadStartDelaySeconds = 0f;
    }
}
