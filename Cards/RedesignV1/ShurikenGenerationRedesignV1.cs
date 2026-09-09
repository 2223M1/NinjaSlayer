using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ShurikenGenerationRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Stock", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<NinjaSlayer.Orbs.ShurikenOrb>(), HoverTipFactory.FromPower<ShurikenGuardRedesignPower>()];

    public ShurikenGenerationRedesignV1()
        : base(nameof(ShurikenGenerationRedesignV1), "ShurikenGuard", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
        await PowerCmd.Apply<ShurikenGuardRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
        await NinjaSlayerCardCmd.ChooseAndDiscard(choiceContext, Owner, 1, this);
    }

    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}
