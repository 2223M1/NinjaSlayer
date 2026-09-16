using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class WaterfallGiantExplosionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_waterfall_giant_explosion";
    public static string Description => "Time the native giant self-destruct damage to the boss burst cue.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim), [typeof(Creature), typeof(string), typeof(float)])];

    public static bool Prefix(Creature creature, string triggerName, ref Task __result)
    {
        if (creature.Monster is not WaterfallGiant || triggerName != "Erupt"
            || creature.CombatState is not { } combat
            || combat.Players.All(player => player.Character is not INinjaSlayerCharacter)
            || combat.RunState.CurrentRoom is not CombatRoom modelRoom
            || NCombatRoom.Instance is not { } room
            || room.GetCreatureNode(creature) is not { } node)
            return true;

        var controller = BossDeathPresentationController.Attach(node, room, null);
        if (!controller.TryPrepareDeathAnimation())
        {
            controller.AbortSetup();
            Entry.Logger.Warn("Waterfall Giant burst capture unavailable; using the native explosion animation.");
            return true;
        }
        BossBurstParticipationRegistry.Mark(node, room, modelRoom, combat.RunState);
        // Only replace the animation wait. Damage, block, Naraku HP, death hooks,
        // and the giant's own Kill command remain in the native ExplodeMove.
        __result = controller.StartExplosion();
        return false;
    }
}
