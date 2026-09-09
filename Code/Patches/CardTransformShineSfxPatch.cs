using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class CardTransformShineSfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_transform_shine_sfx";

    public static string Description => "Play card_transform SFX when hand-card transform shine VFX starts.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCardTransformShineVfx), nameof(NCardTransformShineVfx.PlayAnimation), [typeof(bool)])];

    public static void Prefix()
    {
        SfxCmd.Play(FmodSfx.transform);
    }
}

public sealed class CardTransformShineCleanupPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_transform_shine_cleanup";
    public static string Description => "Release cancelled transform overlays before their parent card is reused from the pool.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCardTransformShineVfx), nameof(NCardTransformShineVfx._ExitTree), [])];

    public static void Postfix(NCardTransformShineVfx __instance) => __instance.QueueFree();
}
