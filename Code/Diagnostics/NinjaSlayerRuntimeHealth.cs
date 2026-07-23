using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Diagnostics;

public static class NinjaSlayerRuntimeHealth
{
    public static NinjaSlayerRuntimeHealthSnapshot Capture()
    {
        IReadOnlyDictionary<string, NinjaSlayerCapabilityHealth> capabilities =
            NinjaSlayerRuntimeHealthSnapshot.FreezeCapabilities(
                NinjaSlayerCapabilityRegistry.Current.Snapshot().Select(pair =>
                    new KeyValuePair<string, NinjaSlayerCapabilityHealth>(
                        pair.Key,
                        new NinjaSlayerCapabilityHealth(
                            pair.Value.State.ToString(),
                            pair.Value.Reason,
                            pair.Value.InstalledPatchCount,
                            pair.Value.IsOperational))));
        RuntimeCounterSnapshot counters = NinjaSlayerRuntimeCounters.Snapshot();
        (bool transitionActive, bool transitionPending) = NinjaSlayerTransitionGate.GetHealthState();

        return new NinjaSlayerRuntimeHealthSnapshot(
            NinjaSlayerRuntimeHealthSnapshot.CurrentSchemaVersion,
            capabilities,
            FinisherSessionRegistry.HasRegisteredSession(),
            transitionActive,
            transitionPending,
            CombatCinematicCameraLease.IsControllingCamera,
            ScreenShakeSuppressionContext.IsSuppressed,
            XAttackAudioContext.SuppressAutomaticSfx,
            XAttackComboContext.Active,
            counters.PreparedApplied,
            counters.PreparedDegraded,
            counters.PreparedRepairFailed,
            counters.FinisherSucceeded,
            counters.FinisherDegraded,
            counters.FinisherFaulted,
            counters.FinisherCancelled,
            counters.TransitionSucceeded,
            counters.TransitionFaulted,
            counters.TransitionCancelled,
            counters.TransitionTimedOut,
            counters.TransitionSuperseded);
    }
}
