using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class BlackFlameRecoveryPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(NarakuLifePower));
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        cardPlay.Card.Owner.Creature == Owner && cardPlay.Card.Type == CardType.Attack
            ? PowerCmd.Apply<NarakuLifePower>(choiceContext, Owner, Amount, Owner, cardPlay.Card)
            : Task.CompletedTask;
    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants) => participants.Contains(Owner)
            ? PowerCmd.Remove(this) : Task.CompletedTask;
}
