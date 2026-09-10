using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Lifecycle;

internal static class NinjaSlayerDrawAnimationBatch
{
    private static readonly AsyncLocal<Batch?> Current = new();

    internal static Lease Enter(Player player)
    {
        Batch? previous = Current.Value;
        if (previous?.Player != player) Current.Value = new Batch(player);
        return new Lease(previous);
    }

    internal static void CardDrawn(CardModel card, bool fromHandDraw)
    {
        if (Current.Value is not { Played: false } batch || batch.Player != card.Owner
            || fromHandDraw || card.Owner.Character is not INinjaSlayerCharacter
            || card.Owner.PlayerCombatState?.Phase != PlayerTurnPhase.Play) return;
        batch.Played = true;
        NinjaSlayerAimPose.Get(card.Owner.Creature)?.BeginBackflip();
    }

    internal sealed class Batch(Player player)
    {
        internal Player Player { get; } = player;
        internal bool Played { get; set; }
    }

    internal readonly struct Lease(Batch? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
