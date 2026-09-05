using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class GreatUkeRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override bool IsPlayable => PileType.Hand.GetPile(Owner).Cards.OfType<ChadoEnergyRedesignV1>().Any();
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Hits", 2), new DynamicVar("Threshold", GreatUkeRedesignPower.DamageThreshold)];

    public GreatUkeRedesignV1()
        : base(nameof(GreatUkeRedesignV1), "OmnidirectionalThrow", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!await NinjaSlayerCardCmd.ChooseAndExhaustRedesignChado(choiceContext, Owner, this))
        {
            return;
        }

        await PowerCmd.Apply<GreatUkeRedesignPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Hits"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() { }
}
