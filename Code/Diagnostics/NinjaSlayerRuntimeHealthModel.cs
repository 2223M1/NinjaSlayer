namespace NinjaSlayer.Code.Diagnostics;

using NinjaSlayer.Code.ExternalAnimations;

public sealed record NinjaSlayerCapabilityHealth(
    string State,
    string Reason,
    int InstalledPatchCount,
    bool IsOperational);

public sealed record NinjaSlayerRuntimeHealthSnapshot(
    Dictionary<string, NinjaSlayerCapabilityHealth> Capabilities,
    bool FinisherSessionActive,
    bool TransitionSessionActive,
    bool TransitionPending,
    bool CinematicCameraActive,
    bool ScreenShakeSuppressed,
    bool XAttackAudioSuppressed,
    bool XAttackComboActive,
    long FinisherCompletions);

internal static class NinjaSlayerRuntimeCounters
{
    private static long _finisherCompletions;

    internal static void RecordFinisher(FinisherCompletionStatus status)
    {
        if (status is FinisherCompletionStatus.Succeeded or FinisherCompletionStatus.Degraded)
        {
            Interlocked.Increment(ref _finisherCompletions);
        }
    }

    internal static long FinisherCompletions => Interlocked.Read(ref _finisherCompletions);
}
