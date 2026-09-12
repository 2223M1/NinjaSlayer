using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Code.Lifecycle;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class NinjaSlayerDrawBatchPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_draw_animation_batch";
    public static string Description => "Group nested card draws into one visual backflip.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardPileCmd), nameof(CardPileCmd.Draw), [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)])];
    public static void Prefix(Player player, bool fromHandDraw, out NinjaSlayerDrawAnimationBatch.Lease __state) =>
        __state = NinjaSlayerDrawAnimationBatch.Enter(player, fromHandDraw);
    public static void Postfix(NinjaSlayerDrawAnimationBatch.Lease __state) => __state.Dispose();
    public static Exception? Finalizer(Exception? __exception, NinjaSlayerDrawAnimationBatch.Lease __state)
    {
        __state.Dispose();
        return __exception;
    }
}

internal sealed class NinjaSlayerDrawBackflipPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_draw_backflip";
    public static string Description => "Start a visual backflip on the first successful extra draw in a batch.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardPileCmd), nameof(CardPileCmd.Add), [typeof(CardModel), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)])];
    public static void Prefix(CardModel card, CardPile newPile) => NinjaSlayerDrawAnimationBatch.MovingToHand(card, newPile);
}
