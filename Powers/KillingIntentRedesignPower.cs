using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Patches;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KillingIntentRedesignPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("KillingIntentPower");

    private bool _receivedDamage;
    private bool _attackIntentAtTurnEnd;

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext, Creature target, DamageResult result,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target == Owner && dealer != null && dealer.Side != Owner.Side && props.IsPoweredAttack()
            && (result.UnblockedDamage > 0 || NarakuLifeDamagePatch.AbsorbedBy(result) > 0))
            _receivedDamage = true;
        return Task.CompletedTask;
    }

    public override Task AfterSideTurnEndLate(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == Owner.Side && participants.Contains(Owner))
            _attackIntentAtTurnEnd = CombatState.HittableEnemies.Any(enemy => enemy.Monster?.IntendsToAttack == true);
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext,
        MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (player != Owner.Player) return;
        if (_attackIntentAtTurnEnd && !_receivedDamage)
        {
            StraightKiRedesignV1 card = CombatState.CreateCard<StraightKiRedesignV1>(player);
            Flash();
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
        }
        await PowerCmd.Remove(this);
    }
}
