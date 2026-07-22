using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.Code.Patches;

internal static class NinjaSlayerPatchCapabilities
{
    public static bool GameplayEnabled => IsOperational(NinjaSlayerCapabilityIds.Gameplay);
    public static bool CardResolutionEnabled => IsOperational(NinjaSlayerCapabilityIds.CardResolution);
    public static bool PreparedSafetyEnabled => IsOperational(NinjaSlayerCapabilityIds.PreparedSafety);
    public static bool PreparedGameplayEnabled => IsOperational(NinjaSlayerCapabilityIds.PreparedGameplay);
    public static bool PreparedUiEnabled => IsOperational(NinjaSlayerCapabilityIds.PreparedUi);
    public static bool FinisherEnabled => IsOperational(NinjaSlayerCapabilityIds.FinisherCore);
    public static bool TransitionEnabled => IsOperational(NinjaSlayerCapabilityIds.TransitionCore);
    public static bool TransitionLoadSmoothingEnabled =>
        IsOperational(NinjaSlayerCapabilityIds.TransitionLoadSmoothing);
    public static bool FeedbackEnabled => IsOperational(NinjaSlayerCapabilityIds.Feedback);

    private static bool IsOperational(string capabilityId) =>
        NinjaSlayerCapabilityRegistry.Current.IsOperational(capabilityId);
}
