namespace NinjaSlayer.Code.Combat;

internal static class RapidAttackTrajectory
{
    public const float ReturnSeconds = 0.2f;

    public static float AddReturnSeconds(float pending, float authored) =>
        Math.Max(0f, pending) + Math.Max(0f, authored);

    public static float RemainingReturnSeconds(float duration, float progress) =>
        Math.Max(0f, duration * (1f - Math.Clamp(progress, 0f, 1f)));
}
