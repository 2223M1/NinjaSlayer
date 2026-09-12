using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class NinjaSlayerFreeControlPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_free_control_end_turn";
    public static string Description => "Restore the free-control body before ending the player's turn.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(PlayerCmd), nameof(PlayerCmd.EndTurn), [typeof(Player), typeof(bool), typeof(Func<Task>)])];
    public static void Prefix(Player player) => NinjaSlayerFreeControl.Get(player.Creature)?.EndTurn();
}
