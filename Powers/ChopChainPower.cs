using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ChopChainPower : RedesignV1CounterPower
{
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));
    public bool UpgradedChop { get; set; }
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), .. NinjaSlayerHoverTips.ExhaustingChop(UpgradedChop)];
    private int _changes;

    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
        decimal amount, Creature? applier, CardModel? cardSource) =>
        power is KaratePower && amount != 0 ? CountChange(choiceContext) : Task.CompletedTask;

    internal async Task CountChange(PlayerChoiceContext choiceContext)
    {
        _changes++;
        if (_changes < 7) return;
        _changes -= 7;
        var chop = Owner.CombatState!.CreateCard<CommonChopRedesignV1>(Owner.Player!);
        if (UpgradedChop) CardCmd.Upgrade(chop);
        chop.AddKeyword(CardKeyword.Exhaust);
        await CardPileCmd.AddGeneratedCardToCombat(chop, PileType.Hand, Owner.Player!);
    }
}
