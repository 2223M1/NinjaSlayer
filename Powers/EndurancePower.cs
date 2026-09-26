using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class EndurancePower : RedesignV1CounterPower
{
    internal static bool HasNotAttackedThisTurn(MegaCrit.Sts2.Core.Entities.Players.Player player) =>
        !CombatManager.Instance.History.CardPlaysFinished.Any(entry =>
            entry.HappenedThisTurn(player.Creature.CombatState!) && entry.CardPlay.Card.Owner == player
            && entry.CardPlay.Card.Type == CardType.Attack);

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(EndurancePower));
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner)) return;
        if (HasNotAttackedThisTurn(Owner.Player!))
            await PowerCmd.Apply<KaratePower>(choiceContext, Owner, Amount, Owner, null);
        await PowerCmd.Remove(this);
    }
}
