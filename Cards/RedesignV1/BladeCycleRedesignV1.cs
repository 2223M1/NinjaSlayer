using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class BladeCycleRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<NinjaSlayer.Orbs.ShurikenOrb>()];

    public BladeCycleRedesignV1()
        : base(nameof(BladeCycleRedesignV1), nameof(BladeCycleRedesignV1), 2, CardType.Power, TargetType.Self) { }

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("StockLoss", 3)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        decimal stockLoss = DynamicVars["StockLoss"].BaseValue;
        var existing = Owner.Creature.GetPower<BladeCyclePower>();
        if (existing is null)
            await PowerCmd.Apply<BladeCyclePower>(choiceContext, Owner.Creature, stockLoss, Owner.Creature, this);
        else if (stockLoss < existing.Amount)
            await PowerCmd.ModifyAmount(choiceContext, existing, stockLoss - existing.Amount, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars["StockLoss"].UpgradeValueBy(-2);
}
