namespace NinjaSlayer.Code.Combat;

internal static class BossBurstCompressionLayout
{
    public const float RetainedRestOffset = 0.12f;

    public static BossFragmentPoint ResolvePackedOrigin(
        BossFragmentPoint burstOrigin,
        BossFragmentPoint restCenter,
        BossFragmentPoint domainRestCenter) =>
        ResolvePackedOrigin(
            burstOrigin,
            new BossFragmentPoint(
                restCenter.X - domainRestCenter.X,
                restCenter.Y - domainRestCenter.Y));

    public static BossFragmentPoint ResolvePackedOrigin(
        BossFragmentPoint burstOrigin,
        BossFragmentPoint restOffset) =>
        new(
            burstOrigin.X + restOffset.X * RetainedRestOffset,
            burstOrigin.Y + restOffset.Y * RetainedRestOffset);
}
