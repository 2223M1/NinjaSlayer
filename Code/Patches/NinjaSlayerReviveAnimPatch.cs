using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerReviveAnimPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_revive_animation_reset";

    public static string Description => "Restore NinjaSlayer visual transforms after revival.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartReviveAnim))
    ];

    public static bool Prefix(NCreature __instance, bool ____isRemotePlayerOrPet)
    {
        if (__instance.Entity.Player?.Character is not INinjaSlayerCharacter || __instance.HasSpineAnimation)
            return true;

        bool flewOut = DeathAnimation.HasFlightVisual(__instance.Entity);
        DeathAnimation.RestoreVisual(__instance.Entity);
        __instance.SetAnimationTrigger("Idle");
        if (!____isRemotePlayerOrPet) __instance.AnimEnableUi();
        __instance.Hitbox.MouseFilter = Godot.Control.MouseFilterEnum.Stop;
        if (flewOut)
        {
            // Presentation-only randomness must not advance a gameplay RNG from
            // a local visual callback (including replay/reconnect).
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(AncientEntranceAnimation.Play(
                __instance.Entity.Player!, AncientEntranceAnimation.FromRoll(Godot.GD.Randf())));
        }
        return false;
    }
}
