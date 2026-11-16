using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NarakuPlayerVisualsPatch : IPatchMethod
{
    public static string PatchId => "naraku_player_visuals";
    public static string Description => "Use the Naraku rig for other characters who accept Naraku.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(Creature), nameof(Creature.CreateVisuals), [])];

    public static bool Prefix(Creature __instance, ref NCreatureVisuals? __result)
    {
        if (TestMode.IsOn || __instance.Player?.Character is INinjaSlayerCharacter
            || !NinjaSlayerFormState.IsFullyReleasedNaraku(__instance)) return true;
        __result = ModelDb.Character<NinjaSlayerCharacter>().CreateVisuals();
        return false;
    }
}

public sealed class NarakuPlayerAnimationPatch : IPatchMethod
{
    public static string PatchId => "naraku_player_animation";
    public static string Description => "Route transformed teammates to the Naraku rig's animation cues.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCreature), nameof(NCreature.SetAnimationTrigger), [typeof(string)])];

    public static bool Prefix(NCreature __instance, string trigger)
    {
        if (__instance.Entity.Player?.Character is INinjaSlayerCharacter
            || !NinjaSlayerFormState.IsFullyReleasedNaraku(__instance.Entity)) return true;
        var overlay = __instance.Visuals.FindChild("NarakuVisualOverlay", recursive: true, owned: false)
            as NarakuVisualOverlay
            ?? throw new InvalidOperationException("A transformed player is missing the Naraku visual rig.");
        overlay.PlayTransformedPlayerAnimation(__instance.Visuals, trigger);
        return false;
    }
}
