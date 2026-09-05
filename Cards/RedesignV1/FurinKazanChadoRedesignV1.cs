using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Code.Commands;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class FurinKazanChadoRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Innate, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<ArtifactPower>(1), new DynamicVar("Breath", 2)];

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
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue);
    }

    protected override void OnUpgrade() => DynamicVars["Breath"].UpgradeValueBy(1);
}
