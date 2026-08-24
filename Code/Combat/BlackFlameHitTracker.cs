using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.Combat;

internal static class BlackFlameHitTracker
{
    private static readonly object ScopeOwner = new();

    public static void Record(CardPlay cardPlay, IEnumerable<DamageResult> results)
    {
        HitSet? hitSet = CardPlayResolutionScope.GetOrCreatePlayState(
            cardPlay,
            ScopeOwner,
            static () => new HitSet());
        if (hitSet is null)
        {
            return;
        }

        foreach (DamageResult result in results)
        {
            hitSet.Targets.Record(result.Receiver, result.TotalDamage);
        }
    }

    public static IReadOnlyList<Creature> TakeLiveOpponents(CardPlay cardPlay)
    {
        if (!CardPlayResolutionScope.TryTakePlayState(cardPlay, ScopeOwner, out HitSet? hitSet))
        {
            return [];
        }

#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
        var playerSide = cardPlay.Card.Owner.Creature.Side;
#else
        var playerSide = cardPlay.Player.Creature.Side;
#endif
        return hitSet.Targets.SnapshotWhere(target =>
            !target.IsDead
            && target.Side != playerSide);
    }

    private sealed class HitSet
    {
        public BlackFlameDamageReceiverSet<Creature> Targets { get; } = new();
    }
}
