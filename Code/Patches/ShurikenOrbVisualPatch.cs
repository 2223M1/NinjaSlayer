using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class ShurikenOrbVisualPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_orb_visual";
    public static string Description => "Initialize the Shuriken's scene without vanilla Spine animation binding.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() => [new(typeof(NOrb), nameof(NOrb.UpdateVisuals), [typeof(bool)])];

    public static void Prefix(NOrb __instance, Control ____visualContainer,
        ref Node2D? ____sprite, ref Tween? ____curTween)
    {
        if (__instance.Model is not ShurikenOrb || !__instance.IsNodeReady()
            || !CombatManager.Instance.IsInProgress || ____sprite is not null)
            return;

        // Vanilla creates every orb as a Spine scene. Our sprite owns its own animation.
        ____sprite = __instance.Model.CreateSprite();
        ____visualContainer.AddChild(____sprite);
        ____sprite.Position = Vector2.Zero;
        ____curTween?.Kill();
        ____curTween = __instance.CreateTween();
        ____curTween.TweenProperty(____sprite, "scale", Vector2.One, 0.5)
            .From(Vector2.Zero).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
