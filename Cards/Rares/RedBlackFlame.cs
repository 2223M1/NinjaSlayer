using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class RedBlackFlame : NarakuThemedCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(RedBlackFlame), 2, CardType.Skill, CardRarity.Rare, TargetType.Self, true);


    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public RedBlackFlame() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await EnsureNarakuForm(choiceContext);

        List<CardModel> attacks = PileType.Hand.GetPile(Owner).Cards
            .Where(card => card.Type == CardType.Attack)
            .ToList();

        foreach (CardModel attack in attacks)
        {
            CardCmd.ApplyKeyword(attack, CardKeyword.Exhaust);
        }

        if (attacks.Count > 0)
        {
            await PowerCmd.Apply<FreeAttackPower>(
                choiceContext,
                Owner.Creature,
                attacks.Count,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
