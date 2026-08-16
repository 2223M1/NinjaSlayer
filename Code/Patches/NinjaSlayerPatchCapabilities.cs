using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.Code.Patches;

internal static class NinjaSlayerPatchCapabilities
{
    public static bool CoreContentEnabled => IsOperational(NinjaSlayerCapabilityIds.CoreContent);
    public static bool OrobasSeaGlassEnabled => IsOperational(NinjaSlayerCapabilityIds.OrobasSeaGlass);
    public static bool RapidCardResolutionEnabled => IsOperational(NinjaSlayerCapabilityIds.RapidCardResolution);
    public static bool BossBurstPresentationEnabled =>
        IsOperational(NinjaSlayerCapabilityIds.BossBurstPresentation);
    public static bool PreparedSafetyEnabled => IsOperational(NinjaSlayerCapabilityIds.PreparedSafety);
    public static bool PreparedGameplayEnabled => IsOperational(NinjaSlayerCapabilityIds.PreparedGameplay);
    public static bool PreparedUiEnabled => IsOperational(NinjaSlayerCapabilityIds.PreparedUi);
    public static bool FinisherEnabled => IsOperational(NinjaSlayerCapabilityIds.FinisherCore);
    public static bool TransitionEnabled => IsOperational(NinjaSlayerCapabilityIds.TransitionCore);
    public static bool TransitionPresentationEnabled =>
        IsOperational(NinjaSlayerCapabilityIds.TransitionPresentation);
    public static bool TransitionLoadSmoothingEnabled =>
        IsOperational(NinjaSlayerCapabilityIds.TransitionLoadSmoothing);
    public static bool FeedbackEnabled => IsOperational(NinjaSlayerCapabilityIds.Feedback);
    public static bool TelemetryIdentityEnabled => IsOperational(NinjaSlayerCapabilityIds.TelemetryIdentity);

    private static bool IsOperational(string capabilityId) =>
        NinjaSlayerCapabilityRegistry.Current.IsOperational(capabilityId);
}
