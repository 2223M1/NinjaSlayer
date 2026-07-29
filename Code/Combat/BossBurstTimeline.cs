namespace NinjaSlayer.Code.Combat;

internal static class BossBurstTimeline
{
    public const float LeadSeconds = 0.9f;
    public const float WhiteoutSeconds = 0.2f;
    public const float WhiteoutStartSeconds = LeadSeconds - WhiteoutSeconds;
    public const float VideoSeconds = 1.875f;
    public const float FadeStartSeconds = 1.5f;

    public static float ResolveFadeAlpha(float videoPosition)
    {
        if (videoPosition <= FadeStartSeconds)
        {
            return 1f;
        }

        float duration = VideoSeconds - FadeStartSeconds;
        if (duration <= 0.0001f)
        {
            return 0f;
        }

        return 1f - Math.Clamp((videoPosition - FadeStartSeconds) / duration, 0f, 1f);
    }

    public static float ResolveWhiteoutMix(float leadPosition)
    {
        float progress = Math.Clamp(
            (leadPosition - WhiteoutStartSeconds) / WhiteoutSeconds,
            0f,
            1f);
        return progress * progress * (3f - 2f * progress);
    }
}
