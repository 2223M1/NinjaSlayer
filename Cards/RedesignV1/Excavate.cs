using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class Excavate : RedesignV1RareCard
{
    public Excavate() : base(nameof(Excavate), "PlaceholderGoldDefense01", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = await CardSelectCmd.FromSimpleGrid(choiceContext,
            PileType.Exhaust.GetPile(Owner).Cards, Owner, new CardSelectorPrefs(SelectionScreenPrompt, 1));
        await CardPileCmd.Add(selected, PileType.Draw, CardPilePosition.Top);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
