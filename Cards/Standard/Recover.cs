using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace NinjaSlayer.Cards.Standard;

public sealed class Recover : NinjaSlayerRareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public Recover() : base(nameof(Recover), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = await CardSelectCmd.FromSimpleGrid(choiceContext,
            PileType.Exhaust.GetPile(Owner).Cards, Owner, new CardSelectorPrefs(SelectionScreenPrompt, 1));
        await CardPileCmd.Add(selected, PileType.Draw, CardPilePosition.Top);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
