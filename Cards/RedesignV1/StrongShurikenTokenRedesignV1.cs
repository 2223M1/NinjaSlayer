using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(TokenCardPool))]
public sealed class StrongShurikenTokenRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(StrongShurikenTokenRedesignV1),
        0,
        CardType.Attack,
        CardRarity.Token,
        TargetType.AnyEnemy,
        false,
        "GiantShurikenCard",
        [NinjaSlayerCardTags.Shuriken]);

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6, ValueProp.Move)];

    public StrongShurikenTokenRedesignV1() : base(Spec) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        ShurikenCombat.BuildAttackCommand(this, cardPlay, DynamicVars.Damage)
            .Execute(choiceContext);

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}
