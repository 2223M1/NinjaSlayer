using System.Collections.ObjectModel;

namespace NinjaSlayer.Code.Diagnostics;

using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Transition;

public sealed record NinjaSlayerCapabilityHealth(
    string State,
    string Reason,
    int InstalledPatchCount,
    bool IsOperational);

public sealed record NinjaSlayerRuntimeHealthSnapshot(
    int SchemaVersion,
    IReadOnlyDictionary<string, NinjaSlayerCapabilityHealth> Capabilities,
    bool FinisherSessionActive,
    bool TransitionSessionActive,
    bool TransitionPending,
    bool CinematicCameraActive,
    bool ScreenShakeSuppressed,
    bool XAttackAudioSuppressed,
    bool XAttackComboActive,
    long PreparedApplied,
    long PreparedDegraded,
    long PreparedRepairFailed,
    long FinisherSucceeded,
    long FinisherDegraded,
    long FinisherFaulted,
    long FinisherCancelled,
    long TransitionSucceeded,
    long TransitionFaulted,
    long TransitionCancelled,
    long TransitionTimedOut,
    long TransitionSuperseded)
{
    public const int CurrentSchemaVersion = 1;

    public static IReadOnlyDictionary<string, NinjaSlayerCapabilityHealth> FreezeCapabilities(
        IEnumerable<KeyValuePair<string, NinjaSlayerCapabilityHealth>> capabilities) =>
        new ReadOnlyDictionary<string, NinjaSlayerCapabilityHealth>(
            capabilities.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
}

internal static class NinjaSlayerRuntimeCounters
{
    private static long _preparedApplied;
    private static long _preparedDegraded;
    private static long _preparedRepairFailed;
    private static long _finisherSucceeded;
    private static long _finisherDegraded;
    private static long _finisherFaulted;
    private static long _finisherCancelled;
    private static long _transitionSucceeded;
    private static long _transitionFaulted;
    private static long _transitionCancelled;
    private static long _transitionTimedOut;
    private static long _transitionSuperseded;

    internal static void RecordPrepared(bool applied, bool degraded, bool repairFailed)
    {
        if (applied)
        {
            Interlocked.Increment(ref _preparedApplied);
        }
        if (degraded)
        {
            Interlocked.Increment(ref _preparedDegraded);
        }
        if (repairFailed)
        {
            Interlocked.Increment(ref _preparedRepairFailed);
        }
    }

    internal static void RecordFinisher(FinisherCompletionStatus status)
    {
        switch (status)
        {
            case FinisherCompletionStatus.Succeeded: Interlocked.Increment(ref _finisherSucceeded); break;
            case FinisherCompletionStatus.Degraded: Interlocked.Increment(ref _finisherDegraded); break;
            case FinisherCompletionStatus.Faulted: Interlocked.Increment(ref _finisherFaulted); break;
            case FinisherCompletionStatus.Cancelled: Interlocked.Increment(ref _finisherCancelled); break;
        }
    }

    internal static void RecordTransition(TransitionCompletionStatus status)
    {
        switch (status)
        {
            case TransitionCompletionStatus.Succeeded: Interlocked.Increment(ref _transitionSucceeded); break;
            case TransitionCompletionStatus.Faulted: Interlocked.Increment(ref _transitionFaulted); break;
            case TransitionCompletionStatus.Cancelled: Interlocked.Increment(ref _transitionCancelled); break;
            case TransitionCompletionStatus.TimedOut: Interlocked.Increment(ref _transitionTimedOut); break;
            case TransitionCompletionStatus.Superseded: Interlocked.Increment(ref _transitionSuperseded); break;
        }
    }

    internal static RuntimeCounterSnapshot Snapshot() => new(
        Interlocked.Read(ref _preparedApplied),
        Interlocked.Read(ref _preparedDegraded),
        Interlocked.Read(ref _preparedRepairFailed),
        Interlocked.Read(ref _finisherSucceeded),
        Interlocked.Read(ref _finisherDegraded),
        Interlocked.Read(ref _finisherFaulted),
        Interlocked.Read(ref _finisherCancelled),
        Interlocked.Read(ref _transitionSucceeded),
        Interlocked.Read(ref _transitionFaulted),
        Interlocked.Read(ref _transitionCancelled),
        Interlocked.Read(ref _transitionTimedOut),
        Interlocked.Read(ref _transitionSuperseded));
}

internal readonly record struct RuntimeCounterSnapshot(
    long PreparedApplied,
    long PreparedDegraded,
    long PreparedRepairFailed,
    long FinisherSucceeded,
    long FinisherDegraded,
    long FinisherFaulted,
    long FinisherCancelled,
    long TransitionSucceeded,
    long TransitionFaulted,
    long TransitionCancelled,
    long TransitionTimedOut,
    long TransitionSuperseded);
