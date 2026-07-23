using NinjaSlayer.Code.Diagnostics;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class RuntimeHealthModelTests
{
    [Fact]
    public void CapabilitySnapshotsAreDetachedAndReadOnly()
    {
        var source = new Dictionary<string, NinjaSlayerCapabilityHealth>
        {
            ["gameplay"] = new("Enabled", "ok", 2, true)
        };

        IReadOnlyDictionary<string, NinjaSlayerCapabilityHealth> snapshot =
            NinjaSlayerRuntimeHealthSnapshot.FreezeCapabilities(source);
        source["gameplay"] = new("Disabled", "changed", 0, false);

        Assert.Equal("Enabled", snapshot["gameplay"].State);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, NinjaSlayerCapabilityHealth>)snapshot).Add(
                "other",
                new NinjaSlayerCapabilityHealth("Enabled", "ok", 1, true)));
    }

    [Fact]
    public void RuntimeCountersRecordOnlyKnownTerminalStatuses()
    {
        RuntimeCounterSnapshot before = NinjaSlayerRuntimeCounters.Snapshot();

        NinjaSlayerRuntimeCounters.RecordPrepared(applied: true, degraded: true, repairFailed: false);
        NinjaSlayerRuntimeCounters.RecordFinisher(FinisherCompletionStatus.Succeeded);
        NinjaSlayerRuntimeCounters.RecordTransition(TransitionCompletionStatus.TimedOut);
        NinjaSlayerRuntimeCounters.RecordTransition(TransitionCompletionStatus.Superseded);
        RuntimeCounterSnapshot after = NinjaSlayerRuntimeCounters.Snapshot();

        Assert.Equal(before.PreparedApplied + 1, after.PreparedApplied);
        Assert.Equal(before.PreparedDegraded + 1, after.PreparedDegraded);
        Assert.Equal(before.FinisherSucceeded + 1, after.FinisherSucceeded);
        Assert.Equal(before.TransitionTimedOut + 1, after.TransitionTimedOut);
        Assert.Equal(before.TransitionSuperseded + 1, after.TransitionSuperseded);
    }
}
