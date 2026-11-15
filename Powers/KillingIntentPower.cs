using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KillingIntentPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("KillingIntentPower");
    private bool[] _rewardUpgrades = [];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        _rewardUpgrades.Distinct().SelectMany(upgraded => HoverTipFactory.FromCardWithCardHoverTips<StraightKi>(upgraded));
    public override LocString Description
    {
        get
        {
            LocString description = base.Description;
            int upgraded = _rewardUpgrades.Count(value => value);
            description.Add("Cards", _rewardUpgrades.Length - upgraded);
            description.Add("UpgradedCards", upgraded);
            return description;
        }
    }

    private bool _receivedDamage;
    private bool _attackIntentAtTurnEnd;

    public void QueueReward(bool upgraded)
    {
        AssertMutable();
        _rewardUpgrades = [.. _rewardUpgrades, upgraded];
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext, Creature target, DamageResult result,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target == Owner && dealer != null && dealer.Side != Owner.Side && props.IsPoweredAttack()
            && result.UnblockedDamage > 0)
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

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player) return;
        await PowerCmd.Remove(this);
        if (_attackIntentAtTurnEnd && !_receivedDamage)
        {
            foreach (bool upgraded in _rewardUpgrades)
            {
                StraightKi card = player.Creature.CombatState!.CreateCard<StraightKi>(player);
                if (upgraded) CardCmd.Upgrade(card);
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
            }
        }
    }
}
