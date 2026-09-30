using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class BladeSweep : NinjaSlayerUncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Stock", 2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];

    public BladeSweep()
        : base(nameof(BladeSweep), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
        await PowerCmd.Apply<BladeSweepPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}
