using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class ScreenShakeSuppressionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_screen_shake_suppression";

    public static string Description => "Route native screen punches through the active NinjaSlayer cinematic camera.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NScreenShake), nameof(NScreenShake.Shake), [typeof(ShakeStrength), typeof(ShakeDuration), typeof(float)])];

    public static bool Prefix(ShakeStrength strength, ShakeDuration duration, float degAngle)
    {
        if (ScreenShakeSuppressionContext.IsSuppressed)
        {
            return false;
        }

        return !CombatCinematicCameraLease.TryRouteScreenShake(strength, duration, degAngle);
    }
}

public sealed class ScreenRumbleCinematicSuppressionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_screen_rumble_cinematic_suppression";

    public static string Description => "Prevent native rumble from outliving a NinjaSlayer cinematic camera lease.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NScreenShake), nameof(NScreenShake.Rumble), [typeof(ShakeStrength), typeof(ShakeDuration), typeof(RumbleStyle)])];

    public static bool Prefix() => !ScreenMotionPatchPolicy.ShouldSuppressUnroutableMotion;
}

public sealed class ScreenTraumaCinematicSuppressionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_screen_trauma_cinematic_suppression";

    public static string Description => "Prevent native trauma from leaking past a NinjaSlayer cinematic camera lease.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NScreenShake), nameof(NScreenShake.AddTrauma), [typeof(ShakeStrength)])];

    public static bool Prefix() => !ScreenMotionPatchPolicy.ShouldSuppressUnroutableMotion;
}

internal static class ScreenMotionPatchPolicy
{
    internal static bool ShouldSuppressUnroutableMotion =>
        ScreenShakeSuppressionContext.IsSuppressed
        || CombatCinematicCameraLease.IsControllingCamera;
}
