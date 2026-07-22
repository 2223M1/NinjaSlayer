using Godot;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossGreetingTimeline
{
    public const float VideoSeconds = 260f / 24f;
    public const float PlayerZoomMultiplier = 2f;
    public const float PlayerCameraLeadSeconds = 0.2f;
    public const float PlayerCameraFollowDelaySeconds = 0.2f;
    public const float PlayerCameraSettleSeconds = 0.12f;
    public const float BossCameraMoveSeconds = 0.2f;
    public const float CameraReturnSeconds = CinematicTimingContract.BossCameraReturnSeconds;
    public const float MinimumBossCameraHoldSeconds = CinematicTimingContract.BossMinimumCameraHoldSeconds;
    public const float BossActionTimeoutSeconds = 8f;
    public const float BossBubbleLifetimeSeconds = 999f;
    public const float PostCombatStartBubbleSeconds = 2f;
    public static readonly Vector2 PlayerFinalCameraOffset = new(0f, -60f);
}
