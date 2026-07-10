using System.Diagnostics;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

public static class NinjaSlayerTransitionLoadSmoothing
{
    internal const int FinalizeBatchSize = 1;

    private static long animationStartedAt;
    private static int finalizedResourceCount;
    private static int finalizeBatchCount;
    private static double longestFinalizeBatchMilliseconds;
    private static int deferredCollectCount;

    internal static bool IsAnimationPlaying { get; private set; }

    internal static void BeginAnimation()
    {
        animationStartedAt = Stopwatch.GetTimestamp();
        finalizedResourceCount = 0;
        finalizeBatchCount = 0;
        longestFinalizeBatchMilliseconds = 0;
        deferredCollectCount = 0;
        IsAnimationPlaying = true;
    }

    internal static void RecordFinalizeBatch(int count, TimeSpan elapsed)
    {
        finalizedResourceCount += count;
        if (count > 0)
        {
            finalizeBatchCount++;
            longestFinalizeBatchMilliseconds = Math.Max(
                longestFinalizeBatchMilliseconds,
                elapsed.TotalMilliseconds);
        }
    }

    public static void CollectWhenSafe()
    {
        if (IsAnimationPlaying)
        {
            deferredCollectCount++;
            return;
        }

        GC.Collect();
    }

    internal static void EndAnimationAndCollectDeferred()
    {
        IsAnimationPlaying = false;

        var elapsed = animationStartedAt == 0
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(animationStartedAt);
        var collectionsToMerge = deferredCollectCount;
        deferredCollectCount = 0;
        animationStartedAt = 0;

        Entry.Logger.Info(
            $"NinjaSlayer transition load smoothing finished: duration={elapsed.TotalMilliseconds:F0}ms, " +
            $"finalized={finalizedResourceCount}, batches={finalizeBatchCount}, " +
            $"longest_batch={longestFinalizeBatchMilliseconds:F2}ms, deferred_gc={collectionsToMerge}.");

        if (collectionsToMerge > 0)
        {
            GC.Collect();
        }
    }
}
