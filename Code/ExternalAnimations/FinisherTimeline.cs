using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal enum FinisherPreviewProfile { A, B, C }

internal static class FinisherTimeline
{
    // B is the production default; the smoke driver can override it before a session is created.
    internal static FinisherPreviewProfile PreviewProfile { get; set; } = FinisherPreviewProfile.B;
    // Frame offsets in the selected 30000/1001 fps reference, not engine constants.
    internal static float ReferenceTime(FinisherPreviewProfile profile, int frameOffset) =>
        frameOffset * (1001f / 30000f) * (profile == FinisherPreviewProfile.B ? 0.5f : 1f);
    internal static float ImpactZoomSeconds(FinisherPreviewProfile profile) => ReferenceTime(profile, 4);
    internal static float ImpactReturnStart(FinisherPreviewProfile profile) => ReferenceTime(profile, 26);
    internal static float ImpactReleaseSeconds(FinisherPreviewProfile profile) => ReferenceTime(profile, 27);
    internal static float ImpactEndSeconds(FinisherPreviewProfile profile) => ReferenceTime(profile, 31);
    internal static float ImpactZoom(float from, float to, float progress)
    {
        float u = Mathf.Clamp(progress, 0f, 1f);
        return 1f / ((1f - u) / from + u / to);
    }
    public const float ImpactLeadSeconds = 0.04f;
    public const float DoomPoseSeconds = 0.3f;
    public const float ImpactRecoverySeconds = 0.1f;
    public const float DeathKickSettleSeconds = 0.1f;
    public const float ReturnSeconds = 0.2f;
    public const float SingleHitZoomSeconds = 0.1f;
    public const float MultiHitZoomSeconds = 0.2f;
    public const float FinalHitZoomSeconds = 0.1f;
    public const float MultiHitZoomMultiplier = 1.6f;
    public const float FinalHitZoomMultiplier = 2f;
    public const float CameraPunchScaleMultiplier = 1.06f;
    public const float CameraPushPixels = 16f;
    public const float WatchdogSeconds = 90f;
    public const float EnemyKnockbackPixels = 30f;
    public const float EnhancedEnemyTiltDegrees = 3f;
    public const float ReverseVictimRotationDegrees = 15f;
    public const float ImpactVfxTargetMargin = 160f;
    public static readonly Vector2 JumpDeathSquash = new(1.2f, 0.55f);
    public static readonly Vector2 DefaultDeathSquash = new(0.55f, 1.2f);
    internal static Vector2 MeleeSquash(FinisherPreviewProfile profile) =>
        profile == FinisherPreviewProfile.A ? DefaultDeathSquash : new(0.50f, 1.20f);

    internal static bool AllowsDeathSquash(Creature victim) =>
        victim.Player?.Character is not INinjaSlayerCharacter
        && victim.Monster?.GetType().Assembly != typeof(FinisherTimeline).Assembly;
}
