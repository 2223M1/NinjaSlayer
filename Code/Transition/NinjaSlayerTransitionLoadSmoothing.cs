namespace NinjaSlayer.Code.Transition;

public static class NinjaSlayerTransitionLoadSmoothing
{
    /// <summary>
    /// Always finalize at least one queued resource so the drain cannot stall, then keep going
    /// while the batch stays inside <see cref="FinalizeBatchBudget"/>. A fixed count could not do
    /// both jobs: one cheap resource per call left most of the queue to drain in a burst at the
    /// reveal, while a larger count would let a single expensive resource overrun the frame.
    /// </summary>
    internal const int FinalizeBatchMinimum = 1;

    internal static readonly TimeSpan FinalizeBatchBudget = TimeSpan.FromMilliseconds(2);

    private static long animationSessionId;

    internal static bool IsAnimationPlaying => Volatile.Read(ref animationSessionId) != 0;

    public static int GetConcurrentAssetLoadLimit() => IsAnimationPlaying ? 8 : 128;

    internal static void BeginSession(long sessionId) =>
        Volatile.Write(ref animationSessionId, sessionId);

    internal static void EndAnimation(long sessionId)
    {
        Interlocked.CompareExchange(ref animationSessionId, 0, sessionId);
    }
}
