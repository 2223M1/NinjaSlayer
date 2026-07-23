namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherDeathKickTimeline
{
    internal static float GetRecoveryProgress(float sharedProgress, float joinedAtProgress)
    {
        float joined = Math.Clamp(joinedAtProgress, 0f, 1f);
        if (joined >= 1f)
        {
            return 1f;
        }

        float localProgress = Math.Clamp((sharedProgress - joined) / (1f - joined), 0f, 1f);
        float remaining = 1f - localProgress;
        return 1f - remaining * remaining * remaining;
    }
}
