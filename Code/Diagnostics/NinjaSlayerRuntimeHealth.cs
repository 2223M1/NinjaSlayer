using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Diagnostics;

public static class NinjaSlayerRuntimeHealth
{
    public static NinjaSlayerRuntimeHealthSnapshot Capture()
    {
        Dictionary<string, NinjaSlayerCapabilityHealth> capabilities =
            NinjaSlayerCapabilityRegistry.Current.Snapshot().ToDictionary(
                pair => pair.Key,
                pair => new NinjaSlayerCapabilityHealth(
                    pair.Value.State.ToString(),
                    pair.Value.Reason,
                    pair.Value.InstalledPatchCount,
                    pair.Value.IsOperational),
                StringComparer.Ordinal);
        (bool transitionActive, bool transitionPending) = NinjaSlayerTransitionGate.GetHealthState();

        return new NinjaSlayerRuntimeHealthSnapshot(
            capabilities,
            FinisherSessionRegistry.HasRegisteredSession(),
            transitionActive,
            transitionPending,
            CombatCinematicCameraLease.IsControllingCamera,
            ScreenShakeSuppressionContext.IsSuppressed,
            XAttackAudioContext.SuppressAutomaticSfx,
            XAttackComboContext.Active,
            NinjaSlayerRuntimeCounters.FinisherCompletions);
    }
}
