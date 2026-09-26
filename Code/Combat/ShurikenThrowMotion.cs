namespace NinjaSlayer.Code.Combat;

internal static class ShurikenThrowMotion
{
    internal const float DurationSeconds = .167f;
    internal const float ReleaseSeconds = .083f;
    internal const float ReleaseProgress = ReleaseSeconds / DurationSeconds;

    internal static (float Degrees, float Distance) Sample(float progress)
    {
        const float release = ReleaseProgress;
        float send = progress / release;
        if (send < .25f)
        {
            float p = Smooth(send / .25f);
            return (-6f * p, -4f * p);
        }
        if (send < 1f)
        {
            float p = Smooth((send - .25f) / .45f);
            return (-6f + 14f * p, -4f + 16f * p);
        }
        float tail = 1f - Smooth((progress - release) / (1f - release));
        return (8f * tail, 12f * tail);
    }

    private static float Smooth(float value)
    {
        float p = Math.Clamp(value, 0f, 1f);
        return p * p * (3f - 2f * p);
    }
}
