using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Models.Events;
using NinjaSlayer.Events;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class AncientEntranceCreatureVisibilityPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_ancient_entrance_creature_visibility";

    public static string Description => "Hide NinjaSlayer before a pending Ancient entrance animation is staged.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature), [typeof(Creature)])];

    public static void Postfix(NCombatRoom __instance, Creature creature)
    {
        if (creature.Player is not { } player
            || !IsNinjaSlayer(player)
            || (!(BossGreetingCinematic.ReplacesAncientEntrance(player)
                    ? BossGreetingCinematic.ShouldStage(player)
                    : NinjaSlayerRunData.HasPendingAncientEntranceAnimation(player))
                && player.RunState.CurrentRoom is not EventRoom { CanonicalEvent: SawatariEvent or TheArchitect }))
        {
            return;
        }

        __instance.GetCreatureNode(creature)?.Visuals.Hide();
    }

    private static bool IsNinjaSlayer(MegaCrit.Sts2.Core.Entities.Players.Player player) =>
        player.Character is INinjaSlayerCharacter;
}
