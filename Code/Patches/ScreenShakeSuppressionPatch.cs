using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class ScreenShakeSuppressionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_screen_shake_suppression";

    public static string Description => "Allow narrow NinjaSlayer card effects to suppress default screen shake.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NGame), nameof(NGame.ScreenShake), [typeof(ShakeStrength), typeof(ShakeDuration), typeof(float)])];

    public static bool Prefix(ShakeStrength strength, ShakeDuration duration, float degrees)
    {
        if (ScreenShakeSuppressionContext.IsSuppressed)
        {
            return false;
        }

        return !CombatCinematicCameraLease.TryRouteScreenShake(strength, duration, degrees);
    }
}
