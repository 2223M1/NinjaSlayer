using Godot;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerFakeMerchantScalePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_fake_merchant_scale";
    public static string Description => "Keep the merchant portrait at its shop size inside Fake Merchant's enlarged combat-visual container.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NFakeMerchant), "AfterRoomIsLoaded", Type.EmptyTypes)];

    public static void Postfix(NFakeMerchant __instance)
    {
        var container = __instance.GetNode<Control>("%CharacterContainer");
        foreach (var visual in container.GetChildren().OfType<NMerchantCharacter>())
        {
            if (visual.GetNodeOrNull<Sprite2D>("Visuals")?.Texture?.ResourcePath
                == NinjaSlayerWorldVisualProfile.Merchant.IdleTexturePath)
                visual.Scale /= container.Scale;
        }
    }
}
