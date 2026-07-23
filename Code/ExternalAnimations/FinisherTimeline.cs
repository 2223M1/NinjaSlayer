using Godot;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherTimeline
{
    public const float ImpactLeadSeconds = 0.04f;
    public const float DoomPoseSeconds = 0.3f;
    public const float ImpactRecoverySeconds = 0.1f;
    public const float DeathKickSettleSeconds = CinematicTimingContract.FinisherDeathKickSettleSeconds;
    public const float ReturnSeconds = CinematicTimingContract.FinisherReturnSeconds;
    public const float SingleHitZoomSeconds = 0.1f;
    public const float MultiHitZoomSeconds = 0.2f;
    public const float FinalHitZoomSeconds = 0.1f;
    public const float MultiHitZoomMultiplier = 1.6f;
    public const float FinalHitZoomMultiplier = 2f;
    public const float CameraPunchScaleMultiplier = 1.06f;
    public const float CameraPushPixels = 16f;
    public const float WatchdogSeconds = CinematicTimingContract.FinisherWatchdogSeconds;
    public const float EnemyKnockbackPixels = 30f;
    public const float EnhancedEnemyTiltDegrees = 3f;
    public const float ImpactVfxTargetMargin = 160f;
    public static readonly Vector2 JumpDeathSquash = new(1.2f, 0.55f);
    public static readonly Vector2 DefaultDeathSquash = new(0.55f, 1.2f);
}
