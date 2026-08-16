using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Combat.SecondaryResources;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ChadoBreathRedesignV1 : NinjaSlayerRedesignCardTemplate, IReturnToHandAfterPlay
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(ChadoBreathRedesignV1), 0, CardType.Skill, CardRarity.Basic, TargetType.Self, true);

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];

    public ChadoBreathRedesignV1() : base(Spec, nameof(Meditation))
    {
        this.SecondaryCosts().Set(NinjaSlayerTeaEnergy.Id, 1);
    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PlayerCmd.GainEnergy(1, Owner);

    protected override void OnUpgrade() { }
}
