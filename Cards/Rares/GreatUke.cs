using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class GreatUke : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(GreatUke), 0, CardType.Skill, CardRarity.Rare, TargetType.Self, true);


    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("Reduction", 3)
    ];

    public GreatUke() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach (CardModel status in PileType.Hand.GetPile(Owner).Cards.Where(c => c.Type == CardType.Status).ToList())
        {
            await CardCmd.Exhaust(choiceContext, status);
        }

        if (PileType.Hand.GetPile(Owner).Cards.Any(c => c is ChadoCard))
        {
            await PowerCmd.Apply<GreatUkePower>(choiceContext, Owner.Creature, DynamicVars["Reduction"].BaseValue, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["Reduction"].UpgradeValueBy(1);
    }
}
