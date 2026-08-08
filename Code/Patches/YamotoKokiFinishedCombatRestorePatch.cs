using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Monsters;
using NinjaSlayer.Relics;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class YamotoKokiFinishedCombatRestorePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_finished_combat_restore";
    public static string Description =>
        "Restore the presentation-only Yamoto Koki companion in loaded combat reward rooms.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCombatRoom), nameof(NCombatRoom._Ready))
    ];

    public static void Postfix(NCombatRoom __instance)
    {
        try
        {
            RestoreIfNeeded(__instance);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Could not restore Yamoto Koki in the combat reward room: {exception}");
        }
    }

    private static void RestoreIfNeeded(NCombatRoom room)
    {
        var runState = room.CreatureNodes
            .Select(node => node.Entity.Player?.RunState)
            .FirstOrDefault(state => state != null);
        if (runState == null)
        {
            return;
        }

        YamotoKokiCuteRelic? controller = YamotoKokiPartyState.GetController(runState);
        bool companionPresent = room.CreatureNodes.Any(
            node => node.Entity.Monster is YamotoKokiMonster);
        if (room.Mode != CombatRoomMode.FinishedCombat
            || controller == null
            || YamotoKokiPartyState.HasPlayedFarewell(runState)
            || companionPresent)
        {
            return;
        }

        YamotoKokiMonster model = (YamotoKokiMonster)ModelDb
            .Monster<YamotoKokiMonster>()
            .ToMutable();
        var companion = new Creature(model, CombatSide.Player, null)
        {
            PetOwner = controller.Owner
        };
        room.AddCreature(companion);
        NCreature? node = room.CreatureNodes.FirstOrDefault(
            candidate => ReferenceEquals(candidate.Entity, companion));
        if (node != null)
        {
            node.ToggleIsInteractable(false);
            node.IntentContainer.Visible = false;
        }

        YamotoKokiAllyLayoutPatch.Reflow(room);
    }
}
