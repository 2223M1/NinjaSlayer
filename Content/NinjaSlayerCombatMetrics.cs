using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class NinjaSlayerCombatMetrics : NinjaSlayerCombatSingletonTemplate
{
    private static ConditionalWeakTable<ICombatState, CombatMetricsSnapshot<Player>> _snapshots = new();

    public static int ChadoGeneratedThisCombat(Player player) =>
        GetSnapshot(player.Creature.CombatState).GeneratedChado(player);

    public static bool ChadoExhaustedThisTurn(Player player) =>
        GetSnapshot(player.Creature.CombatState).ChadoExhausted(player);

    public static bool ChadoDiscardedThisTurn(Player player) =>
        GetSnapshot(player.Creature.CombatState).ChadoDiscarded(player);

    public static bool PreviousFinishedCardWasAttack(Player player) =>
        GetSnapshot(player.Creature.CombatState).PreviousFinishedWasAttack(player);

    public static int MeleeAttacksPlayedThisTurn(Player player) =>
        GetSnapshot(player.Creature.CombatState).MeleeAttacks(player);

    public static bool LostHpThisTurn(Creature creature) => creature.Player is { } player
        && GetSnapshot(creature.CombatState).LostHp(player);

    public override Task BeforeCombatStart()
    {
        if (CombatManager.Instance.DebugOnlyGetState() is { } combatState)
        {
            _ = GetSnapshot(combatState);
        }

        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _snapshots = new ConditionalWeakTable<ICombatState, CombatMetricsSnapshot<Player>>();
        return Task.CompletedTask;
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (card.CombatState is { } state
            && TryGetExisting(state, out CombatMetricsSnapshot<Player>? metrics)
            && metrics is not null)
        {
            EnsureTurn(metrics, state);
            if (card is ChadoCard && creator == card.Owner)
            {
                metrics.AddGeneratedChado(card.Owner);
            }
        }

        return Task.CompletedTask;
    }

    public override Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        if (card.CombatState is { } state
            && TryGetExisting(state, out CombatMetricsSnapshot<Player>? metrics)
            && metrics is not null)
        {
            EnsureTurn(metrics, state);
            if (card is ChadoCard)
            {
                metrics.MarkChadoDiscarded(card.Owner);
            }
        }

        return Task.CompletedTask;
    }

    public override Task AfterCardExhausted(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal)
    {
        if (card.CombatState is { } state
            && TryGetExisting(state, out CombatMetricsSnapshot<Player>? metrics)
            && metrics is not null)
        {
            EnsureTurn(metrics, state);
            if (card is ChadoCard)
            {
                metrics.MarkChadoExhausted(card.Owner);
            }
        }

        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.CombatState is { } state
            && TryGetExisting(state, out CombatMetricsSnapshot<Player>? metrics)
            && metrics is not null)
        {
            EnsureTurn(metrics, state);
            metrics.AddFinishedCard(
                cardPlay.Player,
                cardPlay.Card.Type == CardType.Attack,
                KarateTriggerRules.IsMeleeAttack(cardPlay.Card));
        }

        return Task.CompletedTask;
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (result.UnblockedDamage > 0
            && target.Player is { } player
            && target.CombatState is { } state
            && TryGetExisting(state, out CombatMetricsSnapshot<Player>? metrics)
            && metrics is not null)
        {
            EnsureTurn(metrics, state);
            metrics.MarkHpLost(player);
        }

        return Task.CompletedTask;
    }

    private static CombatMetricsSnapshot<Player> GetSnapshot(ICombatState? combatState)
    {
        if (combatState is null)
        {
            return new CombatMetricsSnapshot<Player>(0, 0);
        }

        if (_snapshots.TryGetValue(combatState, out CombatMetricsSnapshot<Player>? snapshot))
        {
            EnsureTurn(snapshot, combatState);
            return snapshot;
        }

        snapshot = BuildFromHistory(combatState);
        _snapshots.Add(combatState, snapshot);
        return snapshot;
    }

    private static bool TryGetExisting(
        ICombatState combatState,
        out CombatMetricsSnapshot<Player>? snapshot)
    {
        if (_snapshots.TryGetValue(combatState, out snapshot))
        {
            return true;
        }

        // The history entry is already committed before each corresponding hook runs.
        snapshot = BuildFromHistory(combatState);
        _snapshots.Add(combatState, snapshot);
        return false;
    }

    private static CombatMetricsSnapshot<Player> BuildFromHistory(ICombatState combatState)
    {
        var snapshot = new CombatMetricsSnapshot<Player>(combatState.RoundNumber, (int)combatState.CurrentSide);
        foreach (CardGeneratedEntry entry in CombatManager.Instance.History.Entries.OfType<CardGeneratedEntry>())
        {
            if (entry.Creator is { } creator && creator == entry.Card.Owner && entry.Card is ChadoCard)
            {
                snapshot.AddGeneratedChado(creator);
            }
        }

        foreach (CardDiscardedEntry entry in CombatManager.Instance.History.Entries
                     .OfType<CardDiscardedEntry>()
                     .Where(entry => entry.HappenedThisTurn(combatState)))
        {
            if (entry.Card is ChadoCard)
            {
                snapshot.MarkChadoDiscarded(entry.Card.Owner);
            }
        }

        foreach (CardExhaustedEntry entry in CombatManager.Instance.History.Entries
                     .OfType<CardExhaustedEntry>()
                     .Where(entry => entry.HappenedThisTurn(combatState)))
        {
            if (entry.Card is ChadoCard)
            {
                snapshot.MarkChadoExhausted(entry.Card.Owner);
            }
        }

        foreach (DamageReceivedEntry entry in CombatManager.Instance.History.Entries
                     .OfType<DamageReceivedEntry>()
                     .Where(entry => entry.HappenedThisTurn(combatState) && entry.Result.UnblockedDamage > 0))
        {
            if (entry.Receiver.Player is { } player)
            {
                snapshot.MarkHpLost(player);
            }
        }

        foreach (Player player in combatState.Players)
        {
            CardPlayFinishedEntry? previous = CombatManager.Instance.History.CardPlaysFinished
                .LastOrDefault(entry => entry.CardPlay.Player == player);
            if (previous is not null)
            {
                snapshot.AddFinishedCard(player, previous.CardPlay.Card.Type == CardType.Attack, isMelee: false);
            }
        }

        foreach (CardPlayFinishedEntry entry in CombatManager.Instance.History.CardPlaysFinished
                     .Where(entry => entry.HappenedThisTurn(combatState)))
        {
            snapshot.AddFinishedCard(
                entry.CardPlay.Player,
                entry.CardPlay.Card.Type == CardType.Attack,
                KarateTriggerRules.IsMeleeAttack(entry.CardPlay.Card));
        }

        return snapshot;
    }

    private static void EnsureTurn(CombatMetricsSnapshot<Player> snapshot, ICombatState combatState) =>
        snapshot.EnsureTurn(combatState.RoundNumber, (int)combatState.CurrentSide);
}
