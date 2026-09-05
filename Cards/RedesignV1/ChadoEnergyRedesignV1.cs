using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(TokenCardPool))]
public sealed class ChadoEnergyRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(ChadoEnergyRedesignV1),
        1,
        CardType.Skill,
        CardRarity.Token,
        TargetType.Self,
        false,
        "ChadoCard");

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];

    public ChadoEnergyRedesignV1() : base(Spec) { }

    public void IncreaseEnergy(int amount)
    {
        if (amount > 0)
        {
            DynamicVars.Energy.BaseValue += amount;
        }
    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);

    protected override void OnUpgrade() { }
}
