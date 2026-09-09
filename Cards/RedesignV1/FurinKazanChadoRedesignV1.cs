using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class FurinKazanChadoRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<ArtifactPower>()];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<ArtifactPower>(1)];

    public FurinKazanChadoRedesignV1()
        : base(nameof(FurinKazanChadoRedesignV1), "TeaSamadhi", 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ArtifactPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(ArtifactPower)].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
