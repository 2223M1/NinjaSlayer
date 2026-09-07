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
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner)) return;
        bool attacked = CombatManager.Instance.History.CardPlaysFinished.Any(entry =>
            entry.HappenedThisTurn(Owner.CombatState!) && entry.CardPlay.Card.Owner == Owner.Player
            && entry.CardPlay.Card.Type == CardType.Attack);
        if (!attacked) await PowerCmd.Apply<KaratePower>(choiceContext, Owner, Amount, Owner, null);
        await PowerCmd.Remove(this);
    }
}
