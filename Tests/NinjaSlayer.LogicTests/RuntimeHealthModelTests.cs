using NinjaSlayer.Code.Diagnostics;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.LogicTests;

public sealed class RuntimeHealthModelTests
{
    [Fact]
    public void RuntimeCounterRecordsCompletedFinishersOnly()
    {
        long before = NinjaSlayerRuntimeCounters.FinisherCompletions;

        NinjaSlayerRuntimeCounters.RecordFinisher(FinisherCompletionStatus.Succeeded);
        NinjaSlayerRuntimeCounters.RecordFinisher(FinisherCompletionStatus.Faulted);

        Assert.Equal(before + 1, NinjaSlayerRuntimeCounters.FinisherCompletions);
    }
}
